using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Execution;

public sealed class SqlExecutionService : ISqlExecutionService
{
    private sealed record QueryStatementInfo(string Sql, int StartOffset, int Length, int StartLine, int StartColumn);

    private static readonly string ExecutionServiceLogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAnalyzer.Next", "execution-service.log");

    private const int DefaultPreviewRows = 500;
    private const int MaxPreviewRows = 10000;
    private static readonly bool DiagnosticLoggingEnabled = string.Equals(Environment.GetEnvironmentVariable("SQLANALYZER_DIAGNOSTIC_LOGS"), "1", StringComparison.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<int, string> OracleProviderTypeNames = new Dictionary<int, string>
    {
        [101] = "BFile",
        [102] = "Blob",
        [103] = "Byte",
        [104] = "Char",
        [105] = "Clob",
        [106] = "Date",
        [107] = "Decimal",
        [108] = "Double",
        [109] = "Long",
        [110] = "LongRaw",
        [111] = "Int16",
        [112] = "Int32",
        [113] = "Int64",
        [114] = "IntervalDS",
        [115] = "IntervalYM",
        [116] = "NClob",
        [117] = "NChar",
        [119] = "NVarchar2",
        [120] = "Raw",
        [121] = "RefCursor",
        [122] = "Single",
        [123] = "TimeStamp",
        [124] = "TimeStampLTZ",
        [125] = "TimeStampTZ",
        [126] = "Varchar2",
        [127] = "XmlType",
        [128] = "Array",
        [129] = "Object",
        [130] = "Ref",
        [132] = "BinaryDouble",
        [133] = "BinaryFloat",
        [134] = "Boolean",
        [135] = "Json"
    };
    private readonly DbProviderRuntime _runtime;
    public SqlExecutionService(IDatabaseProviderCatalog providerCatalog)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
    }

    // 执行服务负责把“原始 SQL 文本”规范化成可执行语句序列，并把多语句、多结果集重新汇总回统一结果模型。
    public async Task<QueryExecutionResult> ExecuteAsync(QueryExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string connectionName = request.Connection?.Name ?? "(no-connection)";
        AppendExecutionServiceLog($"ExecuteAsync:start; connection={connectionName}; includePlan={request.IncludeExecutionPlan}; sqlLength={request.Sql?.Length ?? 0}; schema={request.Schema ?? string.Empty}");
        if (request.Connection == null)
        {
            return BuildMessageResult("Editor mode: no database connection selected.");
        }

        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return BuildMessageResult("No SQL supplied.");
        }

        IReadOnlyList<QueryStatementInfo> statements = SplitExecutableStatements(request.Sql);
        if (statements.Count == 0)
        {
            return BuildMessageResult("No SQL supplied.");
        }
        QueryStatementInfo firstStatement = statements[0];
        string executableSql = firstStatement.Sql;

        DatabaseProviderDefinition provider = _runtime.GetProvider(request.Connection);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return BuildMessageResult("MongoDB execution migration is not finished yet.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            Stopwatch factoryStopwatch = Stopwatch.StartNew();
            DbProviderFactory factory = _runtime.ResolveFactory(provider, request.Connection);
            factoryStopwatch.Stop();
            await using DbConnection connection = factory.CreateConnection()
                ?? throw new InvalidOperationException("Unable to create database connection.");
            connection.ConnectionString = _runtime.BuildConnectionString(provider, request.Connection);
            Stopwatch openStopwatch = Stopwatch.StartNew();
            await connection.OpenAsync(cancellationToken);
            openStopwatch.Stop();
            AppendExecutionServiceLog($"ExecuteAsync:connection-opened; connection={connectionName}; provider={provider.Name}; factoryMs={factoryStopwatch.ElapsedMilliseconds}; openMs={openStopwatch.ElapsedMilliseconds}");

            if (!string.IsNullOrWhiteSpace(request.Schema))
            {
                Stopwatch schemaStopwatch = Stopwatch.StartNew();
                await ApplySchemaAsync(connection, provider, request.Schema, cancellationToken);
                schemaStopwatch.Stop();
                AppendExecutionServiceLog($"ExecuteAsync:schema-applied; connection={connectionName}; schema={request.Schema}; elapsedMs={schemaStopwatch.ElapsedMilliseconds}");
            }

            int previewLimit = Math.Clamp(request.MaxPreviewRows <= 0 ? DefaultPreviewRows : request.MaxPreviewRows, 1, MaxPreviewRows);
            string selectedSchema = string.IsNullOrWhiteSpace(request.Schema) ? request.Connection.Schema : request.Schema;
            if (statements.Count > 1)
            {
                return await ExecuteStatementBatchAsync(
                    connection,
                    provider,
                    statements,
                    selectedSchema,
                    request.IncludeExecutionPlan,
                    previewLimit,
                    connectionName,
                    request.Sql,
                    request.SqlBaseOffset,
                    cancellationToken);
            }

            string sql = request.IncludeExecutionPlan && !string.IsNullOrWhiteSpace(provider.ExplainPrefix)
                ? provider.Name.Equals("Oracle", StringComparison.OrdinalIgnoreCase)
                    ? $"{provider.ExplainPrefix} {executableSql}"
                    : $"{provider.ExplainPrefix} {executableSql}"
                : executableSql;

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 60;

            if (IsNonQuery(executableSql))
            {
                Stopwatch nonQueryStopwatch = Stopwatch.StartNew();
                int affected = await command.ExecuteNonQueryAsync(cancellationToken);
                nonQueryStopwatch.Stop();
                stopwatch.Stop();
                AppendExecutionServiceLog($"ExecuteAsync:non-query-complete; connection={connectionName}; affected={affected}; commandMs={nonQueryStopwatch.ElapsedMilliseconds}; totalMs={stopwatch.ElapsedMilliseconds}");
                return BuildAffectedRowsResult(affected, stopwatch.Elapsed);
            }

            List<QueryResultSet> resultSets = [];
            int resultIndex = 1;
            bool previewTruncated = false;
            int totalRowsRead = 0;

            Stopwatch readerStopwatch = Stopwatch.StartNew();
            await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                readerStopwatch.Stop();
                AppendExecutionServiceLog($"ExecuteAsync:reader-opened; connection={connectionName}; elapsedMs={readerStopwatch.ElapsedMilliseconds}; previewLimit={previewLimit}");
                do
                {
                    if (reader.FieldCount <= 0)
                    {
                        continue;
                    }

                    Stopwatch resultSetStopwatch = Stopwatch.StartNew();
                    string resultName = $"Result {resultIndex++}";
                    (QueryResultSet resultSet, int rowsRead, bool isPreviewTruncated) = await BuildTabularResultSetAsync(
                        reader,
                        provider,
                        executableSql,
                        selectedSchema,
                        resultName,
                        previewLimit,
                        cancellationToken);
                    resultSets.Add(resultSet);
                    totalRowsRead += rowsRead;
                    previewTruncated = isPreviewTruncated;
                    resultSetStopwatch.Stop();
                    AppendExecutionServiceLog($"ExecuteAsync:resultset-built; connection={connectionName}; name={resultName}; columns={resultSet.Columns.Count}; rows={resultSet.Rows.Count}; elapsedMs={resultSetStopwatch.ElapsedMilliseconds}; truncated={previewTruncated}; canEdit={resultSet.CanEdit}");

                    if (previewTruncated)
                    {
                        break;
                    }
                } while (await reader.NextResultAsync(cancellationToken));
            }

            await PopulateEditableMetadataAsync(connection, provider, resultSets, cancellationToken);

            stopwatch.Stop();

            if (request.IncludeExecutionPlan && provider.Name.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
            {
                Stopwatch planStopwatch = Stopwatch.StartNew();
                resultSets.Add(await LoadOracleExecutionPlanAsync(connection, cancellationToken));
                planStopwatch.Stop();
                AppendExecutionServiceLog($"ExecuteAsync:plan-loaded; connection={connectionName}; elapsedMs={planStopwatch.ElapsedMilliseconds}");
            }

            if (resultSets.Count == 0)
            {
                AppendExecutionServiceLog($"ExecuteAsync:no-result-set; connection={connectionName}; totalMs={stopwatch.ElapsedMilliseconds}");
                return BuildMessageResult("Command completed with no result set.", stopwatch.Elapsed);
            }

            AppendExecutionServiceLog($"ExecuteAsync:complete; connection={connectionName}; totalMs={stopwatch.ElapsedMilliseconds}; resultSets={resultSets.Count}; totalRowsRead={totalRowsRead}; truncated={previewTruncated}");
            return new QueryExecutionResult
            {
                Summary = previewTruncated
                    ? $"Completed with preview of {resultSets.Count} result set(s), first {previewLimit} row(s) loaded."
                    : $"Completed with {resultSets.Count} result set(s).",
                Duration = stopwatch.Elapsed,
                ResultSets = resultSets,
                IsPreviewTruncated = previewTruncated
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppendExecutionServiceLog($"ExecuteAsync:error; connection={connectionName}; totalMs={stopwatch.ElapsedMilliseconds}; error={ex}");
            QueryExecutionErrorInfo errorInfo = BuildExecutionErrorInfo(ex, request.Sql, firstStatement, 1, request.SqlBaseOffset);
            return BuildMessageResult(BuildErrorDisplayText(errorInfo), stopwatch.Elapsed, errorInfo);
        }
    }
    public async Task<EditableResultMutationResult> SaveEditableResultAsync(EditableResultSaveRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEditableMutationRequest(request.Connection, request.TableName, request.Columns, request.Rows);
        DatabaseProviderDefinition provider = _runtime.GetProvider(request.Connection!);
        await using DbConnection connection = await OpenConnectionAsync(provider, request.Connection!, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Schema))
        {
            await ApplySchemaAsync(connection, provider, request.Schema, cancellationToken);
        }

        await using DbTransaction transaction = connection.BeginTransaction();
        int affectedRows = 0;
        int updatedRows = 0;
        foreach (EditableResultRowMutation row in request.Rows)
        {
            IReadOnlyList<EditableResultMutationColumn> changedColumns = request.Columns
                .Where(column => column.IsEditable &&
                                 !column.IsPrimaryKey &&
                                 column.Index >= 0 &&
                                 column.Index < row.CurrentValues.Count &&
                                 column.Index < row.OriginalDisplayValues.Count &&
                                 !string.Equals(row.CurrentValues[column.Index] ?? string.Empty, row.OriginalDisplayValues[column.Index] ?? string.Empty, StringComparison.Ordinal))
                .ToArray();
            if (changedColumns.Count == 0)
            {
                continue;
            }

            await using DbCommand command = BuildUpdateCommand(connection, transaction, provider, request, row, changedColumns);
            affectedRows += await command.ExecuteNonQueryAsync(cancellationToken);
            updatedRows++;
        }

        await transaction.CommitAsync(cancellationToken);
        return new EditableResultMutationResult
        {
            AffectedRows = affectedRows,
            Summary = $"已保存 {updatedRows} 行修改，影响 {affectedRows} 行数据。"
        };
    }
    public async Task<EditableResultMutationResult> DeleteEditableResultRowsAsync(EditableResultDeleteRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEditableMutationRequest(request.Connection, request.TableName, request.Columns, request.Rows);
        DatabaseProviderDefinition provider = _runtime.GetProvider(request.Connection!);
        await using DbConnection connection = await OpenConnectionAsync(provider, request.Connection!, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Schema))
        {
            await ApplySchemaAsync(connection, provider, request.Schema, cancellationToken);
        }

        await using DbTransaction transaction = connection.BeginTransaction();
        int affectedRows = 0;
        foreach (EditableResultRowMutation row in request.Rows)
        {
            await using DbCommand command = BuildDeleteCommand(connection, transaction, provider, request, row);
            affectedRows += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new EditableResultMutationResult
        {
            AffectedRows = affectedRows,
            Summary = $"已删除 {request.Rows.Count} 行数据，影响 {affectedRows} 行。"
        };
    }
    private async Task<DbConnection> OpenConnectionAsync(
        DatabaseProviderDefinition provider,
        ConnectionProfile connectionProfile,
        CancellationToken cancellationToken)
    {
        DbProviderFactory factory = _runtime.ResolveFactory(provider, connectionProfile);
        DbConnection connection = factory.CreateConnection()
            ?? throw new InvalidOperationException("Unable to create database connection.");
        connection.ConnectionString = _runtime.BuildConnectionString(provider, connectionProfile);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task<QueryExecutionResult> ExecuteStatementBatchAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        IReadOnlyList<QueryStatementInfo> statements,
        string? selectedSchema,
        bool includeExecutionPlan,
        int previewLimit,
        string connectionName,
        string executedSql,
        int sqlBaseOffset,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<QueryResultSet> aggregatedResultSets = [];
        bool previewTruncated = false;
        int totalRowsRead = 0;
        int completedStatements = 0;

        for (int statementIndex = 0; statementIndex < statements.Count; statementIndex++)
        {
            QueryStatementInfo statement = statements[statementIndex];
            try
            {
                (IReadOnlyList<QueryResultSet> resultSets, int rowsRead, bool statementPreviewTruncated) = await ExecuteSingleStatementAsync(
                    connection,
                    provider,
                    statement.Sql,
                    selectedSchema,
                    includeExecutionPlan,
                    previewLimit,
                    connectionName,
                    statementIndex + 1,
                    statements.Count,
                    cancellationToken);

                aggregatedResultSets.AddRange(resultSets);
                totalRowsRead += rowsRead;
                completedStatements++;

                if (statementPreviewTruncated)
                {
                    previewTruncated = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                AppendExecutionServiceLog($"ExecuteAsync:statement-error; connection={connectionName}; statement={statementIndex + 1}; error={ex}");
                QueryExecutionErrorInfo errorInfo = BuildExecutionErrorInfo(ex, executedSql, statement, statementIndex + 1, sqlBaseOffset);
                aggregatedResultSets.Add(new QueryResultSet
                {
                    Name = $"Statement {statementIndex + 1} Error",
                    Columns = ["Message"],
                    Rows = [[ BuildErrorDisplayText(errorInfo) ]]
                });
                stopwatch.Stop();
                return new QueryExecutionResult
                {
                    Summary = $"Execution stopped at statement {statementIndex + 1} of {statements.Count}: {errorInfo.Message}",
                    Duration = stopwatch.Elapsed,
                    ResultSets = aggregatedResultSets,
                    IsPreviewTruncated = previewTruncated,
                    Error = errorInfo
                };
            }
        }

        stopwatch.Stop();

        if (aggregatedResultSets.Count == 0)
        {
            return BuildMessageResult($"Completed {completedStatements} statement(s) with no result set.", stopwatch.Elapsed);
        }

        AppendExecutionServiceLog($"ExecuteAsync:batch-complete; connection={connectionName}; statements={completedStatements}/{statements.Count}; resultSets={aggregatedResultSets.Count}; totalRowsRead={totalRowsRead}; truncated={previewTruncated}; totalMs={stopwatch.ElapsedMilliseconds}");
        return new QueryExecutionResult
        {
            Summary = previewTruncated
                ? $"Completed {completedStatements} statement(s), preview limited to first {previewLimit} row(s) of the current result set."
                : $"Completed {completedStatements} statement(s) with {aggregatedResultSets.Count} result set(s).",
            Duration = stopwatch.Elapsed,
            ResultSets = aggregatedResultSets,
            IsPreviewTruncated = previewTruncated
        };
    }
    private async Task<(IReadOnlyList<QueryResultSet> ResultSets, int RowsRead, bool PreviewTruncated)> ExecuteSingleStatementAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string executableSql,
        string? selectedSchema,
        bool includeExecutionPlan,
        int previewLimit,
        string connectionName,
        int statementIndex,
        int statementCount,
        CancellationToken cancellationToken)
    {
        string sql = includeExecutionPlan && !string.IsNullOrWhiteSpace(provider.ExplainPrefix)
            ? provider.Name.Equals("Oracle", StringComparison.OrdinalIgnoreCase)
                ? $"{provider.ExplainPrefix} {executableSql}"
                : $"{provider.ExplainPrefix} {executableSql}"
            : executableSql;

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;

        if (IsNonQuery(executableSql))
        {
            Stopwatch nonQueryStopwatch = Stopwatch.StartNew();
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            nonQueryStopwatch.Stop();
            AppendExecutionServiceLog($"ExecuteAsync:statement-non-query; connection={connectionName}; statement={statementIndex}; affected={affected}; commandMs={nonQueryStopwatch.ElapsedMilliseconds}");
            return
            (
                [
                    new QueryResultSet
                    {
                        Name = statementCount > 1 ? $"Statement {statementIndex} Summary" : "Summary",
                        Columns = ["Message"],
                        Rows = [[ $"Affected rows: {affected}" ]]
                    }
                ],
                1,
                false
            );
        }

        List<QueryResultSet> resultSets = [];
        int resultIndex = 1;
        bool previewTruncated = false;
        int totalRowsRead = 0;

        Stopwatch readerStopwatch = Stopwatch.StartNew();
        await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            readerStopwatch.Stop();
            AppendExecutionServiceLog($"ExecuteAsync:statement-reader-opened; connection={connectionName}; statement={statementIndex}; elapsedMs={readerStopwatch.ElapsedMilliseconds}; previewLimit={previewLimit}");
            do
            {
                if (reader.FieldCount <= 0)
                {
                    continue;
                }

                Stopwatch resultSetStopwatch = Stopwatch.StartNew();
                string name = statementCount > 1
                    ? $"Statement {statementIndex} - Result {resultIndex++}"
                    : $"Result {resultIndex++}";
                (QueryResultSet resultSet, int rowsRead, bool isPreviewTruncated) = await BuildTabularResultSetAsync(
                    reader,
                    provider,
                    executableSql,
                    selectedSchema,
                    name,
                    previewLimit,
                    cancellationToken);

                resultSets.Add(resultSet);
                totalRowsRead += rowsRead;
                previewTruncated = isPreviewTruncated;
                resultSetStopwatch.Stop();
                AppendExecutionServiceLog($"ExecuteAsync:statement-resultset-built; connection={connectionName}; statement={statementIndex}; name={name}; columns={resultSet.Columns.Count}; rows={resultSet.Rows.Count}; elapsedMs={resultSetStopwatch.ElapsedMilliseconds}; truncated={previewTruncated}; canEdit={resultSet.CanEdit}");

                if (previewTruncated)
                {
                    break;
                }
            } while (await reader.NextResultAsync(cancellationToken));
        }

        await PopulateEditableMetadataAsync(connection, provider, resultSets, cancellationToken);

        if (includeExecutionPlan && provider.Name.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            Stopwatch planStopwatch = Stopwatch.StartNew();
            QueryResultSet planResult = await LoadOracleExecutionPlanAsync(connection, cancellationToken);
            planResult.Name = statementCount > 1 ? $"Statement {statementIndex} - {planResult.Name}" : planResult.Name;
            resultSets.Add(planResult);
            planStopwatch.Stop();
            AppendExecutionServiceLog($"ExecuteAsync:statement-plan-loaded; connection={connectionName}; statement={statementIndex}; elapsedMs={planStopwatch.ElapsedMilliseconds}");
        }

        if (resultSets.Count == 0)
        {
            resultSets.Add(new QueryResultSet
            {
                Name = statementCount > 1 ? $"Statement {statementIndex} Message" : "Message",
                Columns = ["Message"],
                Rows = [[ "Command completed with no result set." ]]
            });
        }

        return (resultSets, totalRowsRead, previewTruncated);
    }
    private async Task<(QueryResultSet ResultSet, int RowsRead, bool PreviewTruncated)> BuildTabularResultSetAsync(
        DbDataReader reader,
        DatabaseProviderDefinition provider,
        string executableSql,
        string? selectedSchema,
        string resultName,
        int previewLimit,
        CancellationToken cancellationToken)
    {
        string[] columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        ResultColumnSourceMetadata metadata = GetColumnSourceMetadata(reader, provider, columns, executableSql, selectedSchema);
        List<object?[]> rows = [];
        bool previewTruncated = false;

        // Stop at the preview limit and let the UI ask for a larger run. Pulling a huge result set here hurts both memory and responsiveness.
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= previewLimit)
            {
                previewTruncated = true;
                break;
            }

            object?[] row = new object?[reader.FieldCount];
            for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                object value = reader.GetValue(ordinal);
                row[ordinal] = value == DBNull.Value ? null : value;
            }

            rows.Add(row);
        }

        string? baseSchemaName = metadata.SimpleSelectHint?.SchemaName;
        string? baseTableName = metadata.SimpleSelectHint?.TableName;
        (bool canEdit, string editDisabledReason) = EvaluateEditableResultSet(
            baseTableName,
            metadata.SourceColumns,
            Array.Empty<string>());

        return
        (
            new QueryResultSet
            {
                Name = resultName,
                Columns = columns,
                SourceSchemas = metadata.SourceSchemas,
                SourceTables = metadata.SourceTables,
                SourceColumns = metadata.SourceColumns,
                DataTypeNames = metadata.DataTypeNames,
                ClrTypeNames = metadata.ClrTypeNames,
                BaseSchemaName = baseSchemaName,
                BaseTableName = baseTableName,
                PrimaryKeyColumns = Array.Empty<string>(),
                CanEdit = canEdit,
                EditDisabledReason = editDisabledReason,
                Rows = rows,
                IsPreviewTruncated = previewTruncated,
                PreviewLimit = previewLimit
            },
            rows.Count,
            previewTruncated
        );
    }

    private async Task PopulateEditableMetadataAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        IEnumerable<QueryResultSet> resultSets,
        CancellationToken cancellationToken)
    {
        Dictionary<string, IReadOnlyList<string>> primaryKeyCache = new(StringComparer.OrdinalIgnoreCase);
        foreach (QueryResultSet resultSet in resultSets)
        {
            string? baseSchemaName = resultSet.BaseSchemaName;
            string? baseTableName = resultSet.BaseTableName;
            string cacheKey = $"{baseSchemaName ?? string.Empty}.{baseTableName ?? string.Empty}";
            if (!primaryKeyCache.TryGetValue(cacheKey, out IReadOnlyList<string>? primaryKeyColumns))
            {
                // Several result sets can point at the same table; one primary-key lookup is enough.
                primaryKeyColumns = await LoadPrimaryKeyColumnsAsync(connection, provider, baseSchemaName, baseTableName, cancellationToken);
                primaryKeyCache[cacheKey] = primaryKeyColumns;
            }

            resultSet.PrimaryKeyColumns = primaryKeyColumns;
            (resultSet.CanEdit, resultSet.EditDisabledReason) = EvaluateEditableResultSet(
                baseTableName,
                resultSet.SourceColumns,
                primaryKeyColumns);
        }
    }

    private static async Task ApplySchemaAsync(DbConnection connection, DatabaseProviderDefinition provider, string schema, CancellationToken cancellationToken)
    {
        string trimmedSchema = schema.Trim();
        if (trimmedSchema.Length == 0)
        {
            return;
        }

        string quotedSchema = QuoteIdentifier(provider, trimmedSchema);
        string? sql = provider.Name switch
        {
            "Oracle" => $"ALTER SESSION SET CURRENT_SCHEMA = {quotedSchema}",
            "SqlServer" => null,
            "PostgreSql" => $"SET search_path TO {quotedSchema}",
            "MySql" or "MariaDB" => $"USE {quotedSchema}",
            "KingbaseES" => $"SET search_path TO {quotedSchema}",
            "Dameng" => $"SET SCHEMA {quotedSchema}",
            _ => null
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static bool IsNonQuery(string sql)
    {
        string value = sql.TrimStart();
        return value.StartsWith("insert ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("update ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("delete ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("create ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("alter ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("drop ", StringComparison.OrdinalIgnoreCase);
    }
    private static IReadOnlyList<QueryStatementInfo> SplitExecutableStatements(string sql)
    {
        string trimmed = sql.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<QueryStatementInfo>();
        }

        if (LooksLikePlSqlBlock(trimmed))
        {
            int blockStart = Math.Max(0, sql.IndexOf(trimmed, StringComparison.Ordinal));
            return [CreateStatementInfo(sql, trimmed, blockStart)!];
        }

        List<QueryStatementInfo> statements = [];
        System.Text.StringBuilder builder = new();
        int builderStartOffset = -1;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inBracketIdentifier = false;
        bool inBacktickIdentifier = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int index = 0; index < sql.Length; index++)
        {
            if (builderStartOffset < 0)
            {
                builderStartOffset = index;
            }

            char current = sql[index];
            char next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (inLineComment)
            {
                builder.Append(current);
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                builder.Append(current);
                if (current == '*' && next == '/')
                {
                    builder.Append(next);
                    index++;
                    inBlockComment = false;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBracketIdentifier && !inBacktickIdentifier)
            {
                if (current == '-' && next == '-')
                {
                    builder.Append(current);
                    builder.Append(next);
                    index++;
                    inLineComment = true;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    builder.Append(current);
                    builder.Append(next);
                    index++;
                    inBlockComment = true;
                    continue;
                }
            }

            if (current == '\'' && !inDoubleQuote && !inBracketIdentifier && !inBacktickIdentifier)
            {
                builder.Append(current);
                if (inSingleQuote && next == '\'')
                {
                    builder.Append(next);
                    index++;
                }
                else
                {
                    inSingleQuote = !inSingleQuote;
                }

                continue;
            }

            if (current == '"' && !inSingleQuote && !inBracketIdentifier && !inBacktickIdentifier)
            {
                builder.Append(current);
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (current == '[' && !inSingleQuote && !inDoubleQuote && !inBacktickIdentifier)
            {
                builder.Append(current);
                inBracketIdentifier = true;
                continue;
            }

            if (current == ']' && inBracketIdentifier)
            {
                builder.Append(current);
                inBracketIdentifier = false;
                continue;
            }

            if (current == '`' && !inSingleQuote && !inDoubleQuote && !inBracketIdentifier)
            {
                builder.Append(current);
                inBacktickIdentifier = !inBacktickIdentifier;
                continue;
            }

            if (current == ';' && !inSingleQuote && !inDoubleQuote && !inBracketIdentifier && !inBacktickIdentifier)
            {
                QueryStatementInfo? statement = CreateStatementInfo(sql, builder.ToString(), builderStartOffset);
                if (statement != null)
                {
                    statements.Add(statement);
                }

                builder.Clear();
                builderStartOffset = -1;
                continue;
            }

            builder.Append(current);
        }

        QueryStatementInfo? remaining = CreateStatementInfo(sql, builder.ToString(), builderStartOffset < 0 ? 0 : builderStartOffset);
        if (remaining != null)
        {
            statements.Add(remaining);
        }

        return statements;
    }

    private static QueryStatementInfo? CreateStatementInfo(string sourceSql, string rawSql, int rawStartOffset)
    {
        string statement = NormalizeExecutableSql(rawSql);
        if (string.IsNullOrWhiteSpace(statement))
        {
            return null;
        }

        int leadingWhitespace = rawSql.Length - rawSql.TrimStart().Length;
        int startOffset = Math.Clamp(rawStartOffset + leadingWhitespace, 0, sourceSql.Length);
        (int line, int column) = GetLineColumn(sourceSql, startOffset);
        int maxLength = Math.Max(0, sourceSql.Length - startOffset);
        int length = Math.Min(statement.Length, maxLength);
        return new QueryStatementInfo(statement, startOffset, length, line, column);
    }
    private static string NormalizeExecutableSql(string sql)
    {
        string trimmed = sql.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (LooksLikePlSqlBlock(trimmed))
        {
            return trimmed;
        }

        int end = trimmed.Length;
        while (end > 0)
        {
            while (end > 0 && char.IsWhiteSpace(trimmed[end - 1]))
            {
                end--;
            }

            if (end > 0 && trimmed[end - 1] == ';')
            {
                end--;
                continue;
            }

            break;
        }

        return trimmed[..end].TrimEnd();
    }
    private static bool LooksLikePlSqlBlock(string sql)
    {
        return sql.StartsWith("begin", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("declare", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create or replace procedure", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create or replace function", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create or replace package", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create or replace trigger", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create procedure", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create function", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create package", StringComparison.OrdinalIgnoreCase)
            || sql.StartsWith("create trigger", StringComparison.OrdinalIgnoreCase);
    }
    private static async Task<QueryResultSet> LoadOracleExecutionPlanAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using DbCommand planCommand = connection.CreateCommand();
        planCommand.CommandText = "SELECT PLAN_TABLE_OUTPUT AS PLAN_TEXT FROM TABLE(DBMS_XPLAN.DISPLAY())";
        await using DbDataReader planReader = await planCommand.ExecuteReaderAsync(cancellationToken);

        List<object?[]> rows = [];
        while (await planReader.ReadAsync(cancellationToken))
        {
            rows.Add([planReader["PLAN_TEXT"]?.ToString()]);
        }

        return new QueryResultSet
        {
            Name = "Execution Plan",
            Columns = ["PLAN_TEXT"],
            SourceTables = [null],
            SourceColumns = ["PLAN_TEXT"],
            Rows = rows
        };
    }

    // 结果列本地化严重依赖来源表/列信息，这里尽量从 SchemaTable 中补齐真实来源，拿不到时再安全回退。
    private static ResultColumnSourceMetadata GetColumnSourceMetadata(
        DbDataReader reader,
        DatabaseProviderDefinition provider,
        IReadOnlyList<string> columnNames,
        string executableSql,
        string? defaultSchema)
    {
        string?[] sourceSchemas = new string?[reader.FieldCount];
        string?[] sourceTables = new string?[reader.FieldCount];
        string?[] sourceColumns = new string?[reader.FieldCount];
        string?[] dataTypeNames = new string?[reader.FieldCount];
        string?[] clrTypeNames = new string?[reader.FieldCount];
        SimpleSelectSourceHint? hint = TryParseSimpleSelectSourceHint(executableSql, defaultSchema);

        try
        {
            DataTable? schemaTable = reader.GetSchemaTable();
            if (schemaTable != null)
            {
                for (int ordinal = 0; ordinal < reader.FieldCount && ordinal < schemaTable.Rows.Count; ordinal++)
                {
                    DataRow row = schemaTable.Rows[ordinal];
                    string? schemaName = TryReadSchemaValue(row, "BaseSchemaName")
                        ?? TryReadSchemaValue(row, "SchemaName");
                    string? tableName = TryReadSchemaValue(row, "BaseTableName")
                        ?? TryReadSchemaValue(row, "TableName");
                    string? columnName = TryReadSchemaValue(row, "BaseColumnName")
                        ?? TryReadSchemaValue(row, "ColumnName");

                    if (!string.IsNullOrWhiteSpace(schemaName))
                    {
                        sourceSchemas[ordinal] = schemaName.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(tableName))
                    {
                        sourceTables[ordinal] = tableName.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(columnName))
                    {
                        sourceColumns[ordinal] = columnName.Trim();
                    }

                    string? dataTypeName = ResolveSchemaDataTypeName(row, provider);
                    if (!string.IsNullOrWhiteSpace(dataTypeName))
                    {
                        dataTypeNames[ordinal] = dataTypeName.Trim();
                    }

                    if (row.Table.Columns.Contains("DataType") && row["DataType"] is Type clrType)
                    {
                        clrTypeNames[ordinal] = clrType.FullName;
                    }
                }
            }
        }
        catch
        {
        }

        ApplySingleTableSourceInference(hint, columnNames, sourceSchemas, sourceTables, sourceColumns);
        ApplyColumnSourceFallback(columnNames, sourceColumns);
        return new ResultColumnSourceMetadata(sourceSchemas, sourceTables, sourceColumns, dataTypeNames, clrTypeNames, hint);
    }

    private static string? ResolveSchemaDataTypeName(DataRow row, DatabaseProviderDefinition provider)
    {
        string? dataTypeName = TryReadSchemaValue(row, "DataTypeName");
        if (IsReadableSchemaTypeName(dataTypeName))
        {
            return dataTypeName!.Trim();
        }

        if (!row.Table.Columns.Contains("ProviderType"))
        {
            return null;
        }

        object? providerType = row["ProviderType"];
        if (providerType == null || providerType == DBNull.Value)
        {
            return null;
        }

        if (providerType is Enum providerEnum)
        {
            string enumName = providerEnum.ToString();
            return IsReadableSchemaTypeName(enumName) ? enumName : null;
        }

        string providerText = providerType.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(providerText))
        {
            return null;
        }

        if (!IsNumericSchemaTypeName(providerText))
        {
            return providerText;
        }

        return TryMapNumericProviderType(provider, providerText);
    }

    private static bool IsReadableSchemaTypeName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !IsNumericSchemaTypeName(value.Trim());
    }

    private static bool IsNumericSchemaTypeName(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static string? TryMapNumericProviderType(DatabaseProviderDefinition provider, string providerTypeText)
    {
        if (!int.TryParse(providerTypeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int providerType))
        {
            return null;
        }

        if (string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return OracleProviderTypeNames.TryGetValue(providerType, out string? oracleTypeName)
                ? oracleTypeName
                : null;
        }

        if (string.Equals(provider.Name, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return Enum.GetName(typeof(SqlDbType), providerType);
        }

        return null;
    }

    private static void ApplyColumnSourceFallback(IReadOnlyList<string> columnNames, string?[] sourceColumns)
    {
        for (int ordinal = 0; ordinal < sourceColumns.Length; ordinal++)
        {
            if (!string.IsNullOrWhiteSpace(sourceColumns[ordinal]))
            {
                continue;
            }

            sourceColumns[ordinal] = ordinal < columnNames.Count ? columnNames[ordinal] : null;
        }
    }

    private static void ApplySingleTableSourceInference(
        SimpleSelectSourceHint? hint,
        IReadOnlyList<string> columnNames,
        string?[] sourceSchemas,
        string?[] sourceTables,
        string?[] sourceColumns)
    {
        if (!sourceTables.Any(string.IsNullOrWhiteSpace) && !sourceColumns.Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        if (hint == null)
        {
            return;
        }

        for (int ordinal = 0; ordinal < sourceColumns.Length; ordinal++)
        {
            string outputColumnName = ordinal < columnNames.Count ? columnNames[ordinal] : string.Empty;
            if (string.IsNullOrWhiteSpace(sourceSchemas[ordinal]) && !string.IsNullOrWhiteSpace(hint.SchemaName))
            {
                sourceSchemas[ordinal] = hint.SchemaName;
            }

            if (string.IsNullOrWhiteSpace(sourceTables[ordinal]))
            {
                sourceTables[ordinal] = hint.TableName;
            }

            if (hint.TryResolveOutputColumn(outputColumnName, out string? inferredSourceColumn) &&
                (string.IsNullOrWhiteSpace(sourceColumns[ordinal]) ||
                 string.Equals(sourceColumns[ordinal], outputColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                sourceColumns[ordinal] = inferredSourceColumn;
            }
        }
    }
    private static string? TryReadSchemaValue(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName))
        {
            return null;
        }

        object? value = row[columnName];
        return value == DBNull.Value ? null : value?.ToString();
    }
    private static SimpleSelectSourceHint? TryParseSimpleSelectSourceHint(string sql, string? defaultSchema)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        string sanitizedSql = SanitizeSqlForParsing(sql);
        if (!TryFindTopLevelClause(sanitizedSql, "SELECT", 0, out int selectIndex) ||
            !TryFindTopLevelClause(sanitizedSql, "FROM", selectIndex + 6, out int fromIndex))
        {
            return null;
        }

        string selectSegment = sql[(selectIndex + 6)..fromIndex];
        int fromBodyStart = fromIndex + 4;
        int fromBodyEnd = FindTopLevelClauseBoundary(sanitizedSql, fromBodyStart);
        string fromSegment = sql[fromBodyStart..fromBodyEnd].Trim();
        if (!TryParseSingleTableReference(fromSegment, defaultSchema, out SimpleSelectSourceHint? parsedHint) || parsedHint == null)
        {
            return null;
        }
        SimpleSelectSourceHint hint = parsedHint;

        foreach (string projection in SplitTopLevelCommaSegments(selectSegment))
        {
            hint.RegisterProjection(projection);
        }

        return hint;
    }

    private static string SanitizeSqlForParsing(string sql)
    {
        StringBuilder builder = new StringBuilder(sql.Length);
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inBracket = false;
        bool inBacktick = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int index = 0; index < sql.Length; index++)
        {
            char current = sql[index];
            char next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (inLineComment)
            {
                builder.Append(current == '\r' || current == '\n' ? current : ' ');
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                }
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    builder.Append("  ");
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    builder.Append(char.IsWhiteSpace(current) ? current : ' ');
                }
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBracket && !inBacktick)
            {
                if (current == '-' && next == '-')
                {
                    builder.Append("  ");
                    index++;
                    inLineComment = true;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    builder.Append("  ");
                    index++;
                    inBlockComment = true;
                    continue;
                }
            }

            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'')
                {
                    builder.Append("  ");
                    index++;
                    continue;
                }

                builder.Append(current == '\'' ? '\'' : ' ');
                if (current == '\'')
                {
                    inSingleQuote = false;
                }
                continue;
            }

            if (inDoubleQuote)
            {
                builder.Append(current);
                if (current == '"')
                {
                    inDoubleQuote = false;
                }
                continue;
            }

            if (inBracket)
            {
                builder.Append(current);
                if (current == ']')
                {
                    inBracket = false;
                }
                continue;
            }

            if (inBacktick)
            {
                builder.Append(current);
                if (current == '`')
                {
                    inBacktick = false;
                }
                continue;
            }

            switch (current)
            {
                case '\'':
                    inSingleQuote = true;
                    builder.Append(current);
                    break;
                case '"':
                    inDoubleQuote = true;
                    builder.Append(current);
                    break;
                case '[':
                    inBracket = true;
                    builder.Append(current);
                    break;
                case '`':
                    inBacktick = true;
                    builder.Append(current);
                    break;
                default:
                    builder.Append(current);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryFindTopLevelClause(string sql, string keyword, int startIndex, out int position)
    {
        position = -1;
        int depth = 0;
        for (int index = Math.Max(0, startIndex); index <= sql.Length - keyword.Length; index++)
        {
            char current = sql[index];
            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth != 0)
            {
                continue;
            }

            if (!StartsWithWord(sql, index, keyword))
            {
                continue;
            }

            position = index;
            return true;
        }

        return false;
    }

    private static int FindTopLevelClauseBoundary(string sql, int startIndex)
    {
        string[] keywords =
        [
            "WHERE",
            "GROUP",
            "ORDER",
            "HAVING",
            "UNION",
            "CONNECT",
            "START",
            "MODEL",
            "QUALIFY",
            "FOR",
            "OFFSET",
            "FETCH",
            "LIMIT",
            "WINDOW"
        ];

        int depth = 0;
        for (int index = Math.Max(0, startIndex); index < sql.Length; index++)
        {
            char current = sql[index];
            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth != 0)
            {
                continue;
            }

            if (keywords.Any(keyword => StartsWithWord(sql, index, keyword)))
            {
                return index;
            }
        }

        return sql.Length;
    }

    private static bool StartsWithWord(string text, int index, string keyword)
    {
        if (index < 0 || index + keyword.Length > text.Length)
        {
            return false;
        }

        if (!text.AsSpan(index, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (index == 0 || !IsIdentifierChar(text[index - 1])) &&
               (index + keyword.Length >= text.Length || !IsIdentifierChar(text[index + keyword.Length]));
    }

    private static bool TryParseSingleTableReference(string fromSegment, string? defaultSchema, out SimpleSelectSourceHint? hint)
    {
        hint = null;
        if (string.IsNullOrWhiteSpace(fromSegment))
        {
            return false;
        }

        List<string> tokens = TokenizeSqlSegment(fromSegment);
        if (tokens.Count == 0 ||
            tokens.Any(token => token == "," || token == "(" || token == ")" ||
                token.Equals("JOIN", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("INNER", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("RIGHT", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("FULL", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("CROSS", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        List<string> qualifiedNameParts = [];
        int index = 0;
        while (index < tokens.Count)
        {
            string token = tokens[index];
            if (token == ".")
            {
                index++;
                continue;
            }

            if (!IsIdentifierToken(token))
            {
                break;
            }

            qualifiedNameParts.Add(UnquoteIdentifier(token));
            if (index + 1 < tokens.Count && tokens[index + 1] == ".")
            {
                index += 2;
                continue;
            }

            index++;
            break;
        }

        if (qualifiedNameParts.Count == 0)
        {
            return false;
        }

        string? alias = null;
        if (index < tokens.Count)
        {
            if (tokens[index].Equals("AS", StringComparison.OrdinalIgnoreCase))
            {
                index++;
            }

            if (index < tokens.Count && IsIdentifierToken(tokens[index]))
            {
                alias = UnquoteIdentifier(tokens[index]);
                index++;
            }
        }

        if (index < tokens.Count)
        {
            return false;
        }

        string tableName = qualifiedNameParts[^1];
        string? schemaName = qualifiedNameParts.Count >= 2 ? qualifiedNameParts[^2] : defaultSchema;
        hint = new SimpleSelectSourceHint(schemaName, tableName, alias);
        return true;
    }

    private static IEnumerable<string> SplitTopLevelCommaSegments(string selectSegment)
    {
        List<string> segments = [];
        StringBuilder builder = new StringBuilder();
        int depth = 0;
        bool inDoubleQuote = false;
        bool inBracket = false;
        bool inBacktick = false;

        foreach (char current in selectSegment)
        {
            switch (current)
            {
                case '"':
                    if (!inBracket && !inBacktick)
                    {
                        inDoubleQuote = !inDoubleQuote;
                    }
                    builder.Append(current);
                    continue;
                case '[':
                    if (!inDoubleQuote && !inBacktick)
                    {
                        inBracket = true;
                    }
                    builder.Append(current);
                    continue;
                case ']':
                    if (inBracket)
                    {
                        inBracket = false;
                    }
                    builder.Append(current);
                    continue;
                case '`':
                    if (!inDoubleQuote && !inBracket)
                    {
                        inBacktick = !inBacktick;
                    }
                    builder.Append(current);
                    continue;
                case '(':
                    if (!inDoubleQuote && !inBracket && !inBacktick)
                    {
                        depth++;
                    }
                    builder.Append(current);
                    continue;
                case ')':
                    if (!inDoubleQuote && !inBracket && !inBacktick)
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                    builder.Append(current);
                    continue;
                case ',':
                    if (depth == 0 && !inDoubleQuote && !inBracket && !inBacktick)
                    {
                        segments.Add(builder.ToString());
                        builder.Clear();
                        continue;
                    }
                    builder.Append(current);
                    continue;
                default:
                    builder.Append(current);
                    continue;
            }
        }

        segments.Add(builder.ToString());
        return segments;
    }

    private static List<string> TokenizeSqlSegment(string segment)
    {
        List<string> tokens = [];
        StringBuilder builder = new StringBuilder();
        for (int index = 0; index < segment.Length; index++)
        {
            char current = segment[index];
            if (char.IsWhiteSpace(current))
            {
                FlushToken(tokens, builder);
                continue;
            }

            if (current is '.' or ',' or '(' or ')')
            {
                FlushToken(tokens, builder);
                tokens.Add(current.ToString());
                continue;
            }

            if (current == '"')
            {
                FlushToken(tokens, builder);
                int end = index + 1;
                while (end < segment.Length && segment[end] != '"')
                {
                    end++;
                }
                tokens.Add(segment[index..Math.Min(segment.Length, end + 1)]);
                index = Math.Min(segment.Length - 1, end);
                continue;
            }

            if (current == '[')
            {
                FlushToken(tokens, builder);
                int end = index + 1;
                while (end < segment.Length && segment[end] != ']')
                {
                    end++;
                }
                tokens.Add(segment[index..Math.Min(segment.Length, end + 1)]);
                index = Math.Min(segment.Length - 1, end);
                continue;
            }

            if (current == '`')
            {
                FlushToken(tokens, builder);
                int end = index + 1;
                while (end < segment.Length && segment[end] != '`')
                {
                    end++;
                }
                tokens.Add(segment[index..Math.Min(segment.Length, end + 1)]);
                index = Math.Min(segment.Length - 1, end);
                continue;
            }

            builder.Append(current);
        }

        FlushToken(tokens, builder);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        tokens.Add(builder.ToString());
        builder.Clear();
    }

    private static bool IsIdentifierToken(string token)
    {
        return !string.IsNullOrWhiteSpace(token) &&
               token != "." &&
               token != "," &&
               token != "(" &&
               token != ")";
    }

    private static bool IsIdentifierChar(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_' || value == '$' || value == '#';
    }

    private static string UnquoteIdentifier(string token)
    {
        string trimmed = token.Trim();
        return trimmed.Length >= 2 &&
               ((trimmed[0] == '"' && trimmed[^1] == '"') ||
                (trimmed[0] == '[' && trimmed[^1] == ']') ||
                (trimmed[0] == '`' && trimmed[^1] == '`'))
            ? trimmed[1..^1]
            : trimmed;
    }

    private sealed record ResultColumnSourceMetadata(
        string?[] SourceSchemas,
        string?[] SourceTables,
        string?[] SourceColumns,
        string?[] DataTypeNames,
        string?[] ClrTypeNames,
        SimpleSelectSourceHint? SimpleSelectHint);

    private sealed class SimpleSelectSourceHint
    {
        private readonly Dictionary<string, string> _outputToSourceColumn = new(StringComparer.OrdinalIgnoreCase);

        public string? SchemaName { get; }

        public string TableName { get; }

        public string? Alias { get; }

        public SimpleSelectSourceHint(string? schemaName, string tableName, string? alias)
        {
            SchemaName = string.IsNullOrWhiteSpace(schemaName) ? null : schemaName.Trim();
            TableName = tableName.Trim();
            Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        }

        public void RegisterProjection(string projection)
        {
            string trimmedProjection = StripSqlComments(projection).Trim();
            if (string.IsNullOrWhiteSpace(trimmedProjection))
            {
                return;
            }

            if (trimmedProjection == "*" ||
                (!string.IsNullOrWhiteSpace(Alias) && trimmedProjection.Equals($"{Alias}.*", StringComparison.OrdinalIgnoreCase)) ||
                trimmedProjection.Equals($"{TableName}.*", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            List<string> tokens = TokenizeSqlSegment(trimmedProjection);
            StripLeadingProjectionQualifiers(tokens);
            if (tokens.Count == 0 || tokens.Any(token => token == "(" || token == ")" || token == ","))
            {
                return;
            }

            string? alias = null;
            int aliasIndex = tokens.FindIndex(token => token.Equals("AS", StringComparison.OrdinalIgnoreCase));
            if (aliasIndex >= 0)
            {
                if (aliasIndex + 1 >= tokens.Count || !IsIdentifierToken(tokens[aliasIndex + 1]))
                {
                    return;
                }
                alias = UnquoteIdentifier(tokens[aliasIndex + 1]);
                tokens = tokens.Take(aliasIndex).ToList();
            }
            else if (tokens.Count >= 2 && IsIdentifierToken(tokens[^1]))
            {
                string candidate = tokens[^1];
                string previous = tokens[^2];
                if (previous != "." && !candidate.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    alias = UnquoteIdentifier(candidate);
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }

            List<string> identifierParts = tokens.Where(token => token != ".").Select(UnquoteIdentifier).ToList();
            if (identifierParts.Count == 0 || identifierParts.Count > 3)
            {
                return;
            }

            string sourceColumn = identifierParts[^1];
            string qualifier = identifierParts.Count >= 2 ? identifierParts[^2] : string.Empty;
            if (!string.IsNullOrWhiteSpace(qualifier) &&
                !string.Equals(qualifier, Alias, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(qualifier, TableName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string outputName = string.IsNullOrWhiteSpace(alias) ? sourceColumn : alias;
            _outputToSourceColumn[outputName] = sourceColumn;
        }

        public bool TryResolveOutputColumn(string outputColumnName, out string? sourceColumn)
        {
            sourceColumn = null;
            if (string.IsNullOrWhiteSpace(outputColumnName))
            {
                return false;
            }

            string normalizedOutput = outputColumnName.Trim();
            if (_outputToSourceColumn.TryGetValue(normalizedOutput, out string? mappedColumn))
            {
                sourceColumn = mappedColumn;
                return true;
            }

            sourceColumn = normalizedOutput;
            return true;
        }
    }

    private static void StripLeadingProjectionQualifiers(List<string> tokens)
    {
        while (tokens.Count > 0)
        {
            if (tokens[0].Equals("DISTINCT", StringComparison.OrdinalIgnoreCase) ||
                tokens[0].Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
                tokens[0].Equals("DISTINCTROW", StringComparison.OrdinalIgnoreCase) ||
                tokens[0].Equals("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                tokens[0].Equals("SQL_NO_CACHE", StringComparison.OrdinalIgnoreCase) ||
                tokens[0].Equals("SQL_CACHE", StringComparison.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(0);
                continue;
            }

            if (tokens[0].Equals("TOP", StringComparison.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(0);
                while (tokens.Count > 0 &&
                       (tokens[0] == "(" ||
                        tokens[0] == ")" ||
                        tokens[0].Equals("PERCENT", StringComparison.OrdinalIgnoreCase) ||
                        tokens[0].Equals("WITH", StringComparison.OrdinalIgnoreCase) ||
                        tokens[0].Equals("TIES", StringComparison.OrdinalIgnoreCase) ||
                        int.TryParse(tokens[0], out _)))
                {
                    tokens.RemoveAt(0);
                }

                continue;
            }

            break;
        }
    }

    private static string StripSqlComments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(text.Length);
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inBracket = false;
        bool inBacktick = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            char next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                    builder.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    index++;
                    inBlockComment = false;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBracket && !inBacktick)
            {
                if (current == '-' && next == '-')
                {
                    inLineComment = true;
                    index++;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    index++;
                    continue;
                }
            }

            if (current == '\'' && !inDoubleQuote && !inBracket && !inBacktick)
            {
                builder.Append(current);
                if (inSingleQuote && next == '\'')
                {
                    builder.Append(next);
                    index++;
                }
                else
                {
                    inSingleQuote = !inSingleQuote;
                }

                continue;
            }

            if (current == '"' && !inSingleQuote && !inBracket && !inBacktick)
            {
                inDoubleQuote = !inDoubleQuote;
                builder.Append(current);
                continue;
            }

            if (current == '[' && !inSingleQuote && !inDoubleQuote && !inBacktick)
            {
                inBracket = true;
                builder.Append(current);
                continue;
            }

            if (current == ']' && inBracket)
            {
                inBracket = false;
                builder.Append(current);
                continue;
            }

            if (current == '`' && !inSingleQuote && !inDoubleQuote && !inBracket)
            {
                inBacktick = !inBacktick;
                builder.Append(current);
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
    private static (bool CanEdit, string Reason) EvaluateEditableResultSet(
        string? baseTableName,
        IReadOnlyList<string?> sourceColumns,
        IReadOnlyList<string> primaryKeyColumns)
    {
        if (string.IsNullOrWhiteSpace(baseTableName))
        {
            return (false, "当前查询不是单表查询，不能直接编辑。");
        }

        if (primaryKeyColumns.Count == 0)
        {
            return (false, "目标表未识别到主键，不能直接编辑。");
        }

        HashSet<string> projectedColumns = sourceColumns
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Select(column => column!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!primaryKeyColumns.All(projectedColumns.Contains))
        {
            return (false, "结果集中未包含完整主键列，不能直接编辑。");
        }

        return (true, string.Empty);
    }
    private static async Task<IReadOnlyList<string>> LoadPrimaryKeyColumnsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string? schemaName,
        string? tableName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return Array.Empty<string>();
        }

        string effectiveSchema = string.IsNullOrWhiteSpace(schemaName)
            ? ResolveFallbackSchema(provider)
            : schemaName.Trim();
        if (string.IsNullOrWhiteSpace(effectiveSchema))
        {
            return Array.Empty<string>();
        }

        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, effectiveSchema));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName.Trim()));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select acc.column_name
  from all_constraints ac
  join all_cons_columns acc
    on ac.owner = acc.owner
   and ac.constraint_name = acc.constraint_name
 where ac.constraint_type = 'P'
   and ac.owner = '{escapedSchema}'
   and ac.table_name = '{escapedTable}'",
            "Dameng" => $@"
select acc.column_name
  from all_constraints ac
  join all_cons_columns acc
    on ac.owner = acc.owner
   and ac.constraint_name = acc.constraint_name
 where ac.constraint_type = 'P'
   and ac.owner = '{escapedSchema}'
   and ac.table_name = '{escapedTable}'",
            "SqlServer" => $@"
select kcu.column_name
  from information_schema.table_constraints tc
  join information_schema.key_column_usage kcu
    on tc.constraint_name = kcu.constraint_name
   and tc.table_schema = kcu.table_schema
   and tc.table_name = kcu.table_name
 where tc.constraint_type = 'PRIMARY KEY'
   and tc.table_schema = '{escapedSchema}'
   and tc.table_name = '{escapedTable}'
 order by kcu.ordinal_position",
            "PostgreSql" => $@"
select kcu.column_name
  from information_schema.table_constraints tc
  join information_schema.key_column_usage kcu
    on tc.constraint_name = kcu.constraint_name
   and tc.table_schema = kcu.table_schema
   and tc.table_name = kcu.table_name
 where tc.constraint_type = 'PRIMARY KEY'
   and tc.table_schema = '{escapedSchema}'
   and tc.table_name = '{escapedTable}'
 order by kcu.ordinal_position",
            "KingbaseES" => $@"
select kcu.column_name
  from information_schema.table_constraints tc
  join information_schema.key_column_usage kcu
    on tc.constraint_name = kcu.constraint_name
   and tc.table_schema = kcu.table_schema
   and tc.table_name = kcu.table_name
 where tc.constraint_type = 'PRIMARY KEY'
   and tc.table_schema = '{escapedSchema}'
   and tc.table_name = '{escapedTable}'
 order by kcu.ordinal_position",
            "MySql" or "MariaDB" => $@"
select kcu.column_name
  from information_schema.table_constraints tc
  join information_schema.key_column_usage kcu
    on tc.constraint_name = kcu.constraint_name
   and tc.table_schema = kcu.table_schema
   and tc.table_name = kcu.table_name
 where tc.constraint_type = 'PRIMARY KEY'
   and tc.table_schema = '{escapedSchema}'
   and tc.table_name = '{escapedTable}'
 order by kcu.ordinal_position",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<string>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<string> keys = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                keys.Add(reader.GetString(0));
            }
        }

        return keys;
    }
    private static DbCommand BuildUpdateCommand(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseProviderDefinition provider,
        EditableResultSaveRequest request,
        EditableResultRowMutation row,
        IReadOnlyList<EditableResultMutationColumn> changedColumns)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 60;

        List<string> setSegments = [];
        List<string> whereSegments = [];
        int parameterIndex = 0;

        foreach (EditableResultMutationColumn column in changedColumns)
        {
            string parameterName = $"p{parameterIndex++}";
            setSegments.Add($"{QuoteIdentifier(provider, column.SourceColumn ?? column.HeaderName)} = {BuildParameterPlaceholder(provider, parameterName)}");
            command.Parameters.Add(BuildParameter(command, parameterName, ConvertEditedValue(row.CurrentValues[column.Index], column, row.OriginalValues[column.Index])));
        }

        IReadOnlyList<EditableResultMutationColumn> primaryKeyColumns = request.Columns.Where(column => column.IsPrimaryKey).ToArray();
        foreach (EditableResultMutationColumn primaryKeyColumn in primaryKeyColumns)
        {
            string parameterName = $"p{parameterIndex++}";
            whereSegments.Add($"{QuoteIdentifier(provider, primaryKeyColumn.SourceColumn ?? primaryKeyColumn.HeaderName)} = {BuildParameterPlaceholder(provider, parameterName)}");
            command.Parameters.Add(BuildParameter(command, parameterName, row.OriginalValues[primaryKeyColumn.Index]));
        }

        command.CommandText = $"update {BuildQualifiedName(provider, request.Schema, request.TableName)} set {string.Join(", ", setSegments)} where {string.Join(" and ", whereSegments)}";
        return command;
    }
    private static DbCommand BuildDeleteCommand(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseProviderDefinition provider,
        EditableResultDeleteRequest request,
        EditableResultRowMutation row)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 60;

        List<string> whereSegments = [];
        int parameterIndex = 0;
        foreach (EditableResultMutationColumn primaryKeyColumn in request.Columns.Where(column => column.IsPrimaryKey))
        {
            string parameterName = $"p{parameterIndex++}";
            whereSegments.Add($"{QuoteIdentifier(provider, primaryKeyColumn.SourceColumn ?? primaryKeyColumn.HeaderName)} = {BuildParameterPlaceholder(provider, parameterName)}");
            command.Parameters.Add(BuildParameter(command, parameterName, row.OriginalValues[primaryKeyColumn.Index]));
        }

        command.CommandText = $"delete from {BuildQualifiedName(provider, request.Schema, request.TableName)} where {string.Join(" and ", whereSegments)}";
        return command;
    }
    private static void ValidateEditableMutationRequest(
        ConnectionProfile? connection,
        string tableName,
        IReadOnlyList<EditableResultMutationColumn> columns,
        IReadOnlyList<EditableResultRowMutation> rows)
    {
        if (connection == null)
        {
            throw new InvalidOperationException("当前没有可用的数据库连接。");
        }

        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new InvalidOperationException("当前结果集未识别到目标表，不能保存修改。");
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException("当前结果集缺少列元数据，不能保存修改。");
        }

        if (!columns.Any(static column => column.IsPrimaryKey))
        {
            throw new InvalidOperationException("当前结果集未包含完整主键列，不能保存修改。");
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("当前没有需要处理的结果行。");
        }
    }
    private static object? ConvertEditedValue(string text, EditableResultMutationColumn column, object? originalValue)
    {
        string normalizedText = text ?? string.Empty;
        bool isExplicitNull = string.Equals(normalizedText, "(null)", StringComparison.OrdinalIgnoreCase);
        bool isStringLike = IsStringLikeColumn(column, originalValue);
        if (isExplicitNull)
        {
            return DBNull.Value;
        }

        if (string.IsNullOrEmpty(normalizedText))
        {
            return isStringLike ? string.Empty : DBNull.Value;
        }

        Type? targetType = originalValue?.GetType();
        if (targetType == null && !string.IsNullOrWhiteSpace(column.ClrTypeName))
        {
            targetType = Type.GetType(column.ClrTypeName!, throwOnError: false);
        }

        if (targetType == typeof(string) || isStringLike)
        {
            return normalizedText;
        }

        if (targetType == typeof(DateTime) || IsDateLikeColumn(column))
        {
            if (DateTime.TryParse(normalizedText, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dateTime) ||
                DateTime.TryParse(normalizedText, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
            {
                return dateTime;
            }

            throw new InvalidOperationException($"字段 {column.HeaderName} 的时间值格式无效：{normalizedText}");
        }

        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return normalizedText switch
            {
                "1" => true,
                "0" => false,
                _ when bool.TryParse(normalizedText, out bool boolValue) => boolValue,
                _ => throw new InvalidOperationException($"字段 {column.HeaderName} 的布尔值格式无效：{normalizedText}")
            };
        }

        if (targetType == typeof(Guid) || targetType == typeof(Guid?))
        {
            if (Guid.TryParse(normalizedText, out Guid guidValue))
            {
                return guidValue;
            }

            throw new InvalidOperationException($"字段 {column.HeaderName} 的 GUID 格式无效：{normalizedText}");
        }

        if (IsIntegralType(targetType) || IsIntegerLikeColumn(column))
        {
            if (long.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue) ||
                long.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.CurrentCulture, out longValue))
            {
                return ConvertIntegralValue(longValue, targetType);
            }

            throw new InvalidOperationException($"字段 {column.HeaderName} 的整数格式无效：{normalizedText}");
        }

        if (IsFloatingType(targetType) || IsNumericLikeColumn(column))
        {
            if (decimal.TryParse(normalizedText, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue) ||
                decimal.TryParse(normalizedText, NumberStyles.Float, CultureInfo.CurrentCulture, out decimalValue))
            {
                return ConvertFloatingValue(decimalValue, targetType);
            }

            throw new InvalidOperationException($"字段 {column.HeaderName} 的数值格式无效：{normalizedText}");
        }

        return normalizedText;
    }

    private static DbParameter BuildParameter(DbCommand command, string parameterName, object? value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    private static string BuildParameterPlaceholder(DatabaseProviderDefinition provider, string parameterName)
    {
        return provider.Name switch
        {
            "Oracle" or "Dameng" => $":{parameterName}",
            _ => $"@{parameterName}"
        };
    }

    private static object ConvertIntegralValue(long value, Type? targetType)
    {
        if (targetType == typeof(short) || targetType == typeof(short?))
        {
            return Convert.ToInt16(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(int) || targetType == typeof(int?))
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static object ConvertFloatingValue(decimal value, Type? targetType)
    {
        if (targetType == typeof(float) || targetType == typeof(float?))
        {
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(double) || targetType == typeof(double?))
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static bool IsIntegralType(Type? targetType)
    {
        return targetType == typeof(short) ||
               targetType == typeof(short?) ||
               targetType == typeof(int) ||
               targetType == typeof(int?) ||
               targetType == typeof(long) ||
               targetType == typeof(long?);
    }

    private static bool IsFloatingType(Type? targetType)
    {
        return targetType == typeof(decimal) ||
               targetType == typeof(decimal?) ||
               targetType == typeof(float) ||
               targetType == typeof(float?) ||
               targetType == typeof(double) ||
               targetType == typeof(double?);
    }

    private static bool IsStringLikeColumn(EditableResultMutationColumn column, object? originalValue)
    {
        if (originalValue is string)
        {
            return true;
        }

        string normalizedType = NormalizeTypeName(column.DataTypeName);
        return normalizedType.Contains("char", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("clob", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDateLikeColumn(EditableResultMutationColumn column)
    {
        string normalizedType = NormalizeTypeName(column.DataTypeName);
        return normalizedType.Contains("date", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("time", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIntegerLikeColumn(EditableResultMutationColumn column)
    {
        string normalizedType = NormalizeTypeName(column.DataTypeName);
        return normalizedType is "number" or "integer" or "int" or "smallint" or "bigint" or "tinyint";
    }

    private static bool IsNumericLikeColumn(EditableResultMutationColumn column)
    {
        string normalizedType = NormalizeTypeName(column.DataTypeName);
        return normalizedType.Contains("numeric", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("decimal", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("float", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("double", StringComparison.OrdinalIgnoreCase) ||
               normalizedType.Contains("real", StringComparison.OrdinalIgnoreCase) ||
               normalizedType == "number";
    }

    private static string NormalizeTypeName(string? dataTypeName)
    {
        if (string.IsNullOrWhiteSpace(dataTypeName))
        {
            return string.Empty;
        }

        string normalized = dataTypeName.Trim();
        int separatorIndex = normalized.IndexOfAny(['(', ' ']);
        return separatorIndex >= 0 ? normalized[..separatorIndex].ToLowerInvariant() : normalized.ToLowerInvariant();
    }

    private static string ResolveFallbackSchema(DatabaseProviderDefinition provider)
    {
        return provider.Name switch
        {
            "PostgreSql" or "KingbaseES" => "public",
            "SQLite" => "main",
            _ => string.Empty
        };
    }

    private static string NormalizeMetadataIdentifier(DatabaseProviderDefinition provider, string value)
    {
        return provider.Name switch
        {
            "Oracle" or "Dameng" => value.ToUpperInvariant(),
            _ => value
        };
    }

    private static string BuildQualifiedName(DatabaseProviderDefinition provider, string schemaName, string tableName)
    {
        string quotedTable = QuoteIdentifier(provider, tableName);
        return string.IsNullOrWhiteSpace(schemaName)
            ? quotedTable
            : $"{QuoteIdentifier(provider, schemaName)}.{quotedTable}";
    }

    private static string QuoteIdentifier(DatabaseProviderDefinition provider, string identifier)
    {
        string normalized = identifier?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("结果集缺少必要的表或列名，不能生成数据修改语句。");
        }

        return provider.Name switch
        {
            "SqlServer" => $"[{normalized.Replace("]", "]]")}]",
            "MySql" or "MariaDB" => $"`{normalized.Replace("`", "``")}`",
            _ => $"\"{normalized.Replace("\"", "\"\"")}\""
        };
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private static QueryExecutionErrorInfo BuildExecutionErrorInfo(
        Exception exception,
        string executedSql,
        QueryStatementInfo? statement,
        int statementIndex,
        int sqlBaseOffset)
    {
        statement ??= new QueryStatementInfo(executedSql ?? string.Empty, 0, executedSql?.Length ?? 0, 1, 1);
        int relativeOffset = ResolveProviderErrorOffset(exception, statement.Sql, out bool isInferred);
        relativeOffset = Math.Clamp(relativeOffset, 0, Math.Max(0, statement.Sql.Length));
        int absoluteOffsetInExecutedSql = Math.Clamp(statement.StartOffset + relativeOffset, 0, executedSql?.Length ?? 0);
        (int relativeLine, int relativeColumn) = GetLineColumn(statement.Sql, relativeOffset);
        (int absoluteLine, int absoluteColumn) = GetLineColumn(executedSql ?? string.Empty, absoluteOffsetInExecutedSql);

        return new QueryExecutionErrorInfo
        {
            Message = exception.Message,
            StatementIndex = Math.Max(1, statementIndex),
            StatementStartOffset = sqlBaseOffset + statement.StartOffset,
            RelativeLine = relativeLine,
            RelativeColumn = relativeColumn,
            AbsoluteLine = absoluteLine,
            AbsoluteColumn = absoluteColumn,
            AbsoluteOffset = sqlBaseOffset + absoluteOffsetInExecutedSql,
            ErrorCode = ResolveProviderErrorCode(exception),
            SqlState = ResolveProviderSqlState(exception),
            IsPositionInferred = isInferred
        };
    }

    private static int ResolveProviderErrorOffset(Exception exception, string statementSql, out bool isInferred)
    {
        isInferred = false;
        int? postgresPosition = ReadIntProperty(exception, "Position");
        if (postgresPosition is > 0)
        {
            return postgresPosition.Value - 1;
        }

        int? lineNumber = ReadIntProperty(exception, "LineNumber");
        if (lineNumber is > 0)
        {
            int linePositionColumn = ReadIntProperty(exception, "LinePosition", "ColumnNumber") ?? 1;
            return GetOffsetFromLineColumn(statementSql, lineNumber.Value, Math.Max(1, linePositionColumn));
        }

        if (TryExtractLineColumn(exception.Message, out int line, out int column))
        {
            return GetOffsetFromLineColumn(statementSql, line, column);
        }

        isInferred = true;
        return 0;
    }

    private static bool TryExtractLineColumn(string message, out int line, out int column)
    {
        line = 1;
        column = 1;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        Match lineColumnMatch = Regex.Match(message, @"(?:line|行)\s*(\d+)[^\d]+(?:column|col|列)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (lineColumnMatch.Success &&
            int.TryParse(lineColumnMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out line) &&
            int.TryParse(lineColumnMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out column))
        {
            line = Math.Max(1, line);
            column = Math.Max(1, column);
            return true;
        }

        Match lineMatch = Regex.Match(message, @"(?:line|行)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (lineMatch.Success &&
            int.TryParse(lineMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out line))
        {
            line = Math.Max(1, line);
            column = 1;
            return true;
        }

        return false;
    }

    private static string ResolveProviderErrorCode(Exception exception)
    {
        int? numericCode = ReadIntProperty(exception, "Number", "Code", "ErrorCode");
        return numericCode.HasValue ? numericCode.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string ResolveProviderSqlState(Exception exception)
    {
        return ReadStringProperty(exception, "SqlState", "SQLState") ?? string.Empty;
    }

    private static int? ReadIntProperty(Exception exception, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            object? value = exception.GetType().GetProperty(propertyName)?.GetValue(exception);
            if (value is int intValue)
            {
                return intValue;
            }

            if (value != null && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? ReadStringProperty(Exception exception, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            object? value = exception.GetType().GetProperty(propertyName)?.GetValue(exception);
            string? text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static (int Line, int Column) GetLineColumn(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (1, 1);
        }

        offset = Math.Clamp(offset, 0, text.Length);
        int line = 1;
        int column = 1;
        for (int index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static int GetOffsetFromLineColumn(string text, int line, int column)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        line = Math.Max(1, line);
        column = Math.Max(1, column);
        int currentLine = 1;
        int currentColumn = 1;
        for (int index = 0; index < text.Length; index++)
        {
            if (currentLine == line && currentColumn == column)
            {
                return index;
            }

            if (text[index] == '\n')
            {
                currentLine++;
                currentColumn = 1;
            }
            else
            {
                currentColumn++;
            }
        }

        return text.Length;
    }

    private static string BuildErrorDisplayText(QueryExecutionErrorInfo errorInfo)
    {
        StringBuilder builder = new();
        builder.Append(errorInfo.Message);
        builder.AppendLine();
        builder.Append(FormattableString.Invariant($"语句: {errorInfo.StatementIndex}, 行: {errorInfo.RelativeLine}, 列: {errorInfo.RelativeColumn}"));
        if (!string.IsNullOrWhiteSpace(errorInfo.SqlState))
        {
            builder.Append(FormattableString.Invariant($", SQLSTATE: {errorInfo.SqlState}"));
        }

        if (!string.IsNullOrWhiteSpace(errorInfo.ErrorCode))
        {
            builder.Append(FormattableString.Invariant($", 错误码: {errorInfo.ErrorCode}"));
        }

        if (errorInfo.IsPositionInferred)
        {
            builder.Append(" (位置为推断结果)");
        }

        return builder.ToString();
    }

    private static QueryExecutionResult BuildAffectedRowsResult(int affectedRows, TimeSpan duration)
    {
        return new QueryExecutionResult
        {
            Summary = $"Command completed. Affected rows: {affectedRows}.",
            Duration = duration,
            ResultSets =
            [
                new QueryResultSet
                {
                    Name = "Summary",
                    Columns = ["Message"],
                    Rows = [[ $"Affected rows: {affectedRows}" ]]
                }
            ]
        };
    }
    private static QueryExecutionResult BuildMessageResult(string message, TimeSpan? duration = null, QueryExecutionErrorInfo? error = null)
    {
        return new QueryExecutionResult
        {
            Summary = message,
            Duration = duration ?? TimeSpan.Zero,
            Error = error,
            ResultSets =
            [
                new QueryResultSet
                {
                    Name = "Message",
                    Columns = ["Message"],
                    Rows = [[ message ]]
                }
            ]
        };
    }
    private static void AppendExecutionServiceLog(string message)
    {
        if (!DiagnosticLoggingEnabled)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(ExecutionServiceLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllLines(ExecutionServiceLogPath, [$"{DateTime.Now:O} {message}"]);
        }
        catch
        {
        }
    }
}
