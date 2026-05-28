using System.Data.Common;
using System.Text;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;

public sealed class CommentMaintenanceService : ICommentMaintenanceService
{
    private const long MaxCsvImportBytes = 20L * 1024L * 1024L;
    private const int MaxCsvImportRows = 200000;
    private const int MaxCsvLineLength = 65536;
    private const int MaxCsvFieldLength = 4000;
    private const int MaxCsvImportErrors = 500;
    private const int MetadataCommandTimeoutSeconds = 120;
    private readonly DbProviderRuntime _runtime;
    public CommentMaintenanceService(IDatabaseProviderCatalog providerCatalog)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
    }
    public async Task<CommentMaintenanceWorkspace> LoadWorkspaceAsync(ConnectionProfile profile, string schemaName, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        string effectiveSchema = ResolveEffectiveSchema(provider, profile, schemaName);

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        IReadOnlyList<CommentMaintenanceTableEntry> allTables = await LoadTablesAsync(connection, provider.Name, effectiveSchema, cancellationToken);
        IReadOnlyList<CommentMaintenanceTableEntry> tables = FilterBusinessTables(provider.Name, allTables);
        IReadOnlyList<CommentMaintenanceColumnEntry> columns = FilterBusinessColumns(provider.Name, await LoadColumnsAsync(connection, provider.Name, effectiveSchema, cancellationToken), tables);

        return new CommentMaintenanceWorkspace
        {
            ProviderName = provider.Name,
            ConnectionProfileId = profile.Id,
            ConnectionName = profile.Name,
            SchemaName = effectiveSchema,
            LoadedAt = DateTimeOffset.UtcNow,
            Tables = tables,
            Columns = columns
        };
    }
    public async Task<CommentImportResult> ImportCsvAsync(CommentMaintenanceWorkspace workspace, string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCsvImportFile(filePath);

        Dictionary<string, CommentMaintenanceTableEntry> tableLookup = workspace.Tables.ToDictionary(item => BuildTableKey(item.SchemaName, item.ObjectName, item.ObjectType), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CommentMaintenanceColumnEntry> columnLookup = workspace.Columns.ToDictionary(item => BuildColumnKey(item.SchemaName, item.TableName, item.ColumnName), StringComparer.OrdinalIgnoreCase);

        int imported = 0;
        int updated = 0;
        int skipped = 0;
        List<CommentImportErrorItem> errors = [];
        int index = 0;

        await foreach (string line in File.ReadLinesAsync(filePath, Encoding.UTF8, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            if (index == 1)
            {
                continue;
            }

            if (index > MaxCsvImportRows + 1)
            {
                throw new InvalidDataException($"CSV 行数超过上限 {MaxCsvImportRows}，已停止导入。");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Length > MaxCsvLineLength)
            {
                AddImportError(errors, index, $"CSV 单行长度超过上限 {MaxCsvLineLength}，已跳过。");
                continue;
            }

            string[] values = ParseCsvLine(line);
            if (values.Length < 7)
            {
                AddImportError(errors, index, "CSV 列数不足，无法解析。");
                continue;
            }

            if (values.Any(value => (value?.Length ?? 0) > MaxCsvFieldLength))
            {
                AddImportError(errors, index, $"CSV 字段长度超过上限 {MaxCsvFieldLength}，已跳过。");
                continue;
            }

            string schema = values[0].Trim();
            string targetType = values[1].Trim();
            string objectType = values[2].Trim();
            string objectName = values[3].Trim();
            string columnName = values[4].Trim();
            string editedComment = values[6] ?? string.Empty;
            string normalizedTargetType = NormalizeObjectType(targetType);
            string normalizedObjectType = string.IsNullOrWhiteSpace(objectType) ? normalizedTargetType : NormalizeObjectType(objectType);

            if (!string.Equals(schema, workspace.SchemaName, StringComparison.OrdinalIgnoreCase))
            {
                AddImportError(errors, index, $"CSV 模式 {schema} 与当前模式 {workspace.SchemaName} 不一致。");
                continue;
            }

            if (string.Equals(normalizedTargetType, "column", StringComparison.OrdinalIgnoreCase))
            {
                string key = BuildColumnKey(schema, objectName, columnName);
                if (!columnLookup.TryGetValue(key, out CommentMaintenanceColumnEntry? column))
                {
                    AddImportError(errors, index, $"未找到字段 {objectName}.{columnName}。");
                    continue;
                }

                string columnEditedComment = column.EditedComment;
                ApplyImportedComment(column.CurrentComment, ref columnEditedComment, editedComment, ref imported, ref updated, ref skipped);
                column.EditedComment = columnEditedComment;
                continue;
            }

            if (string.Equals(normalizedTargetType, "table", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedTargetType, "view", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedTargetType, "materialized view", StringComparison.OrdinalIgnoreCase))
            {
                string key = BuildTableKey(schema, objectName, normalizedObjectType);
                if (!tableLookup.TryGetValue(key, out CommentMaintenanceTableEntry? table))
                {
                    AddImportError(errors, index, $"未找到对象 {objectName}。");
                    continue;
                }

                string tableEditedComment = table.EditedComment;
                ApplyImportedComment(table.CurrentComment, ref tableEditedComment, editedComment, ref imported, ref updated, ref skipped);
                table.EditedComment = tableEditedComment;
                continue;
            }

            AddImportError(errors, index, $"不支持的目标类型 {targetType}。");
        }

        return new CommentImportResult
        {
            ImportedCount = imported,
            UpdatedCount = updated,
            SkippedCount = skipped,
            Errors = errors
        };
    }

    private static void ValidateCsvImportFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("未找到要导入的 CSV 文件。", filePath);
        }

        FileInfo fileInfo = new(filePath);
        if (fileInfo.Length > MaxCsvImportBytes)
        {
            throw new InvalidDataException($"CSV 文件超过上限 {MaxCsvImportBytes / 1024 / 1024}MB，已拒绝导入。");
        }
    }

    private static void AddImportError(ICollection<CommentImportErrorItem> errors, int rowNumber, string message)
    {
        if (errors.Count >= MaxCsvImportErrors)
        {
            return;
        }

        errors.Add(new CommentImportErrorItem { RowNumber = rowNumber, Message = message });
    }
    public async Task ExportCsvAsync(CommentMaintenanceWorkspace workspace, string filePath, CancellationToken cancellationToken = default)
    {
        StringBuilder builder = new();
        builder.AppendLine("schema,target_type,object_type,object_name,column_name,current_comment,edited_comment,data_type");

        foreach (CommentMaintenanceTableEntry table in workspace.Tables.OrderBy(item => item.ObjectName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(BuildCsvRow(table.SchemaName, table.ObjectType, table.ObjectType, table.ObjectName, string.Empty, table.CurrentComment, table.EditedComment, string.Empty));
        }

        foreach (CommentMaintenanceColumnEntry column in workspace.Columns.OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ColumnName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(BuildCsvRow(column.SchemaName, "column", column.ObjectType, column.TableName, column.ColumnName, column.CurrentComment, column.EditedComment, column.DataType));
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), new UTF8Encoding(true), cancellationToken);
    }
    public IReadOnlyList<CommentSqlPreviewItem> BuildSqlPreview(CommentMaintenanceWorkspace workspace)
    {
        List<CommentSqlPreviewItem> items = [];

        foreach (CommentMaintenanceTableEntry table in workspace.Tables)
        {
            CommentChangeStatus status = ResolveStatus(table.CurrentComment, table.EditedComment);
            if (status == CommentChangeStatus.Unchanged || status == CommentChangeStatus.ImportFailed)
            {
                continue;
            }

            string sql = BuildTableCommentSql(workspace.ProviderName, table.SchemaName, table.ObjectName, table.ObjectType, table.EditedComment);
            if (string.IsNullOrWhiteSpace(sql))
            {
                continue;
            }

            items.Add(new CommentSqlPreviewItem
            {
                TargetType = NormalizeObjectType(table.ObjectType),
                SchemaName = table.SchemaName,
                ObjectName = table.ObjectName,
                SqlText = sql
            });
        }

        foreach (CommentMaintenanceColumnEntry column in workspace.Columns)
        {
            CommentChangeStatus status = ResolveStatus(column.CurrentComment, column.EditedComment);
            if (status == CommentChangeStatus.Unchanged || status == CommentChangeStatus.ImportFailed)
            {
                continue;
            }

            string sql = BuildColumnCommentSql(workspace.ProviderName, column);
            if (string.IsNullOrWhiteSpace(sql))
            {
                continue;
            }

            items.Add(new CommentSqlPreviewItem
            {
                TargetType = $"column:{NormalizeObjectType(column.ObjectType)}",
                SchemaName = column.SchemaName,
                ObjectName = column.TableName,
                ColumnName = column.ColumnName,
                SqlText = sql
            });
        }

        return items;
    }
    public async Task<int> ApplyChangesAsync(ConnectionProfile profile, IReadOnlyList<CommentSqlPreviewItem> sqlItems, CancellationToken cancellationToken = default)
    {
        if (sqlItems.Count == 0)
        {
            return 0;
        }

        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        int executed = 0;

        try
        {
            foreach (CommentSqlPreviewItem item in sqlItems)
            {
                await using DbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = NormalizeExecutableSql(item.SqlText);
                try
                {
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    string target = string.IsNullOrWhiteSpace(item.ColumnName)
                        ? $"{item.SchemaName}.{item.ObjectName}"
                        : $"{item.SchemaName}.{item.ObjectName}.{item.ColumnName}";
                    throw new InvalidOperationException($"执行注释更新失败: {target}。{ex.Message}", ex);
                }
                executed++;
            }

            await transaction.CommitAsync(cancellationToken);
            return executed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    private static string NormalizeExecutableSql(string sqlText)
    {
        string text = sqlText?.Trim() ?? string.Empty;
        while (text.EndsWith(";", StringComparison.Ordinal))
        {
            text = text[..^1].TrimEnd();
        }

        return text;
    }
    private async Task<DbConnection> OpenConnectionAsync(DatabaseProviderDefinition provider, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        DbProviderFactory factory = _runtime.ResolveFactory(provider, profile);
        DbConnection connection = factory.CreateConnection()
            ?? throw new InvalidOperationException($"Unable to create connection for provider '{provider.Name}'.");
        connection.ConnectionString = _runtime.BuildConnectionString(provider, profile);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
    private static string ResolveEffectiveSchema(DatabaseProviderDefinition provider, ConnectionProfile profile, string schemaName)
    {
        if (!string.IsNullOrWhiteSpace(schemaName))
        {
            return schemaName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profile.Schema))
        {
            return profile.Schema.Trim();
        }

        return provider.Name switch
        {
            "MySql" => profile.Database?.Trim() ?? string.Empty,
            "PostgreSql" or "KingbaseES" => "public",
            "Oracle" or "Dameng" => profile.UserName?.Trim() ?? string.Empty,
            "SqlServer" => "dbo",
            _ => string.Empty
        };
    }
    private static void ApplyImportedComment(string currentComment, ref string editedComment, string importedComment, ref int imported, ref int updated, ref int skipped)
    {
        string current = currentComment?.Trim() ?? string.Empty;
        string next = importedComment?.Trim() ?? string.Empty;
        if (string.Equals(editedComment?.Trim() ?? string.Empty, next, StringComparison.Ordinal))
        {
            skipped++;
            return;
        }

        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            skipped++;
            editedComment = importedComment ?? string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            imported++;
        }
        else
        {
            updated++;
        }

        editedComment = importedComment ?? string.Empty;
    }
    private static async Task<IReadOnlyList<CommentMaintenanceTableEntry>> LoadTablesAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        string sql = BuildTableQuery(providerName, schemaName);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandTimeout = MetadataCommandTimeoutSeconds;
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<CommentMaintenanceTableEntry> items = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CommentMaintenanceTableEntry
            {
                SchemaName = ReadString(reader, "SCHEMA_NAME", schemaName),
                ObjectName = ReadString(reader, "OBJECT_NAME"),
                ObjectType = NormalizeObjectType(ReadString(reader, "OBJECT_TYPE")),
                CurrentComment = ReadString(reader, "COMMENTS"),
                EditedComment = ReadString(reader, "COMMENTS")
            });
        }

        return items.Where(item => !string.IsNullOrWhiteSpace(item.ObjectName))
            .OrderBy(item => item.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private static async Task<IReadOnlyList<CommentMaintenanceColumnEntry>> LoadColumnsAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        string sql = BuildColumnQuery(providerName, schemaName);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandTimeout = MetadataCommandTimeoutSeconds;
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<CommentMaintenanceColumnEntry> items = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CommentMaintenanceColumnEntry
            {
                SchemaName = ReadString(reader, "SCHEMA_NAME", schemaName),
                TableName = ReadString(reader, "TABLE_NAME"),
                ObjectType = NormalizeObjectType(ReadString(reader, "OBJECT_TYPE", "table")),
                ColumnName = ReadString(reader, "COLUMN_NAME"),
                DataType = ReadString(reader, "DATA_TYPE"),
                FullTypeDefinition = ReadString(reader, "FULL_TYPE_DEFINITION"),
                IsNullable = ReadBoolean(reader, "IS_NULLABLE", true),
                DefaultValue = ReadString(reader, "DEFAULT_VALUE"),
                IsIdentity = ReadBoolean(reader, "IS_IDENTITY", false),
                IsComputed = ReadBoolean(reader, "IS_COMPUTED", false),
                ExtraDefinition = ReadString(reader, "EXTRA_DEFINITION"),
                CurrentComment = ReadString(reader, "COMMENTS"),
                EditedComment = ReadString(reader, "COMMENTS")
            });
        }

        return items.Where(item => !string.IsNullOrWhiteSpace(item.TableName) && !string.IsNullOrWhiteSpace(item.ColumnName))
            .OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private static IReadOnlyList<CommentMaintenanceTableEntry> FilterBusinessTables(string providerName, IReadOnlyList<CommentMaintenanceTableEntry> tables)
    {
        return tables
            .Where(item => string.Equals(NormalizeObjectType(item.ObjectType), "table", StringComparison.OrdinalIgnoreCase))
            .Where(item => !IsSystemLikeTable(providerName, item.ObjectName))
            .OrderBy(item => item.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private static IReadOnlyList<CommentMaintenanceColumnEntry> FilterBusinessColumns(
        string providerName,
        IReadOnlyList<CommentMaintenanceColumnEntry> columns,
        IReadOnlyList<CommentMaintenanceTableEntry> businessTables)
    {
        HashSet<string> tableNames = businessTables
            .Select(item => item.ObjectName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return columns
            .Where(item => string.Equals(NormalizeObjectType(item.ObjectType), "table", StringComparison.OrdinalIgnoreCase))
            .Where(item => tableNames.Contains(item.TableName))
            .Where(item => !IsSystemLikeTable(providerName, item.TableName))
            .OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private static bool IsSystemLikeTable(string providerName, string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return true;
        }

        string name = objectName.Trim().ToUpperInvariant();
        return providerName switch
        {
            "Oracle" or "Dameng" => name.StartsWith("BIN$", StringComparison.Ordinal) ||
                                    name.StartsWith("MLOG$", StringComparison.Ordinal) ||
                                    name.StartsWith("RUPD$", StringComparison.Ordinal) ||
                                    name.StartsWith("AQ$", StringComparison.Ordinal) ||
                                    name.StartsWith("DR$", StringComparison.Ordinal) ||
                                    name.StartsWith("SYS_", StringComparison.Ordinal) ||
                                    name.StartsWith("LOGMNR", StringComparison.Ordinal) ||
                                    name.StartsWith("ISEQ$$_", StringComparison.Ordinal),
            "PostgreSql" or "KingbaseES" => name.StartsWith("PG_", StringComparison.Ordinal) ||
                                            name.StartsWith("SQL_", StringComparison.Ordinal),
            "MySql" => name.StartsWith("SYS_", StringComparison.Ordinal),
            "SqlServer" => name.StartsWith("SYS", StringComparison.Ordinal) ||
                           name.StartsWith("QUEUE_", StringComparison.Ordinal),
            _ => false
        };
    }

    private static string BuildTableQuery(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" or "Dameng" => $"""
                select owner as SCHEMA_NAME,
                       table_name as OBJECT_NAME,
                       case table_type when 'TABLE' then 'TABLE' when 'VIEW' then 'VIEW' else table_type end as OBJECT_TYPE,
                       coalesce(comments, '') as COMMENTS
                  from all_tab_comments
                 where owner = '{schema}'
                   and table_type in ('TABLE', 'VIEW')
                 order by table_name
                """,
            "PostgreSql" or "KingbaseES" => $"""
                select n.nspname as SCHEMA_NAME,
                       c.relname as OBJECT_NAME,
                       case c.relkind when 'r' then 'TABLE' when 'v' then 'VIEW' when 'm' then 'MATERIALIZED VIEW' else c.relkind::text end as OBJECT_TYPE,
                       coalesce(obj_description(c.oid, 'pg_class'), '') as COMMENTS
                  from pg_class c
                  join pg_namespace n on n.oid = c.relnamespace
                 where n.nspname = '{schema}'
                   and c.relkind in ('r', 'v', 'm')
                 order by c.relname
                """,
            "MySql" => $"""
                select table_schema as SCHEMA_NAME,
                       table_name as OBJECT_NAME,
                       'TABLE' as OBJECT_TYPE,
                       coalesce(table_comment, '') as COMMENTS
                  from information_schema.tables
                 where table_schema = '{schema}'
                   and table_type = 'BASE TABLE'
                 order by table_name
                """,
            "SqlServer" => $"""
                select s.name as SCHEMA_NAME,
                       o.name as OBJECT_NAME,
                       case o.type when 'U' then 'TABLE' when 'V' then 'VIEW' else o.type_desc end as OBJECT_TYPE,
                       coalesce(cast(ep.value as nvarchar(max)), '') as COMMENTS
                  from sys.objects o
                  join sys.schemas s on s.schema_id = o.schema_id
                  left join sys.extended_properties ep
                    on ep.major_id = o.object_id
                   and ep.minor_id = 0
                   and ep.name = 'MS_Description'
                 where s.name = '{schema}'
                   and o.type in ('U', 'V')
                 order by o.name
                """,
            _ => string.Empty
        };
    }

    private static string BuildColumnQuery(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" => $"""
                select c.owner as SCHEMA_NAME,
                       c.table_name as TABLE_NAME,
                       coalesce(case o.object_type when 'TABLE' then 'TABLE' when 'VIEW' then 'VIEW' when 'MATERIALIZED VIEW' then 'MATERIALIZED VIEW' else o.object_type end, 'TABLE') as OBJECT_TYPE,
                       c.column_name as COLUMN_NAME,
                       c.data_type as DATA_TYPE,
                       c.data_type as FULL_TYPE_DEFINITION,
                       case c.nullable when 'Y' then 1 else 0 end as IS_NULLABLE,
                       c.data_default as DEFAULT_VALUE,
                       0 as IS_IDENTITY,
                       0 as IS_COMPUTED,
                       '' as EXTRA_DEFINITION,
                       coalesce(cc.comments, '') as COMMENTS
                  from all_tab_columns c
                  left join all_col_comments cc
                    on cc.owner = c.owner and cc.table_name = c.table_name and cc.column_name = c.column_name
                  left join all_objects o
                    on o.owner = c.owner and o.object_name = c.table_name and o.object_type in ('TABLE', 'VIEW', 'MATERIALIZED VIEW')
                 where c.owner = '{schema}'
                 order by c.table_name, c.column_id
                """,
            "Dameng" => $"""
                select c.owner as SCHEMA_NAME,
                       c.table_name as TABLE_NAME,
                       coalesce(case o.object_type when 'TABLE' then 'TABLE' when 'VIEW' then 'VIEW' when 'MATERIALIZED VIEW' then 'MATERIALIZED VIEW' else o.object_type end, 'TABLE') as OBJECT_TYPE,
                       c.column_name as COLUMN_NAME,
                       c.data_type as DATA_TYPE,
                       c.data_type as FULL_TYPE_DEFINITION,
                       case c.nullable when 'Y' then 1 else 0 end as IS_NULLABLE,
                       coalesce(trim(c.data_default), '') as DEFAULT_VALUE,
                       0 as IS_IDENTITY,
                       0 as IS_COMPUTED,
                       '' as EXTRA_DEFINITION,
                       coalesce(cc.comments, '') as COMMENTS
                  from all_tab_columns c
                  left join all_col_comments cc
                    on cc.owner = c.owner and cc.table_name = c.table_name and cc.column_name = c.column_name
                  left join all_objects o
                    on o.owner = c.owner and o.object_name = c.table_name and o.object_type in ('TABLE', 'VIEW', 'MATERIALIZED VIEW')
                 where c.owner = '{schema}'
                 order by c.table_name, c.column_id
                """,
            "PostgreSql" or "KingbaseES" => $"""
                select n.nspname as SCHEMA_NAME,
                       cls.relname as TABLE_NAME,
                       case cls.relkind when 'r' then 'TABLE' when 'v' then 'VIEW' when 'm' then 'MATERIALIZED VIEW' else cls.relkind::text end as OBJECT_TYPE,
                       a.attname as COLUMN_NAME,
                       format_type(a.atttypid, a.atttypmod) as DATA_TYPE,
                       format_type(a.atttypid, a.atttypmod) as FULL_TYPE_DEFINITION,
                       case when a.attnotnull then 0 else 1 end as IS_NULLABLE,
                       coalesce(pg_get_expr(ad.adbin, ad.adrelid), '') as DEFAULT_VALUE,
                       0 as IS_IDENTITY,
                       0 as IS_COMPUTED,
                       '' as EXTRA_DEFINITION,
                       coalesce(col_description(cls.oid, a.attnum), '') as COMMENTS
                  from pg_attribute a
                  join pg_class cls on cls.oid = a.attrelid
                  join pg_namespace n on n.oid = cls.relnamespace
                  left join pg_attrdef ad on ad.adrelid = cls.oid and ad.adnum = a.attnum
                 where n.nspname = '{schema}'
                   and cls.relkind in ('r', 'v', 'm')
                   and a.attnum > 0
                   and not a.attisdropped
                 order by cls.relname, a.attnum
                """,
            "MySql" => $"""
                select c.table_schema as SCHEMA_NAME,
                       c.table_name as TABLE_NAME,
                       'TABLE' as OBJECT_TYPE,
                       c.column_name as COLUMN_NAME,
                       c.data_type as DATA_TYPE,
                       concat(
                           c.column_type,
                           case when c.character_set_name is not null then concat(' character set ', c.character_set_name) else '' end,
                           case when c.collation_name is not null then concat(' collate ', c.collation_name) else '' end
                       ) as FULL_TYPE_DEFINITION,
                       case when c.is_nullable = 'YES' then 1 else 0 end as IS_NULLABLE,
                       coalesce(c.column_default, '') as DEFAULT_VALUE,
                       case when lower(c.extra) like '%auto_increment%' then 1 else 0 end as IS_IDENTITY,
                       case when coalesce(c.generation_expression, '') <> '' then 1 else 0 end as IS_COMPUTED,
                       coalesce(c.extra, '') as EXTRA_DEFINITION,
                       coalesce(c.column_comment, '') as COMMENTS
                  from information_schema.columns c
                  join information_schema.tables t on t.table_schema = c.table_schema and t.table_name = c.table_name
                 where c.table_schema = '{schema}'
                   and t.table_type = 'BASE TABLE'
                 order by c.table_name, c.ordinal_position
                """,
            "SqlServer" => $"""
                select s.name as SCHEMA_NAME,
                       o.name as TABLE_NAME,
                       case o.type when 'U' then 'TABLE' when 'V' then 'VIEW' else o.type_desc end as OBJECT_TYPE,
                       c.name as COLUMN_NAME,
                       ty.name as DATA_TYPE,
                       case
                           when ty.name in ('nvarchar', 'nchar') then ty.name + '(' + case when c.max_length = -1 then 'max' else cast(c.max_length / 2 as varchar(10)) end + ')'
                           when ty.name in ('varchar', 'char', 'varbinary', 'binary') then ty.name + '(' + case when c.max_length = -1 then 'max' else cast(c.max_length as varchar(10)) end + ')'
                           when ty.name in ('decimal', 'numeric') then ty.name + '(' + cast(c.precision as varchar(10)) + ',' + cast(c.scale as varchar(10)) + ')'
                           when ty.name in ('datetime2', 'time', 'datetimeoffset') then ty.name + '(' + cast(c.scale as varchar(10)) + ')'
                           else ty.name
                       end as FULL_TYPE_DEFINITION,
                       case when c.is_nullable = 1 then 1 else 0 end as IS_NULLABLE,
                       coalesce(dc.definition, '') as DEFAULT_VALUE,
                       case when c.is_identity = 1 then 1 else 0 end as IS_IDENTITY,
                       case when c.is_computed = 1 then 1 else 0 end as IS_COMPUTED,
                       '' as EXTRA_DEFINITION,
                       coalesce(cast(ep.value as nvarchar(max)), '') as COMMENTS
                  from sys.columns c
                  join sys.objects o on o.object_id = c.object_id and o.type in ('U', 'V')
                  join sys.schemas s on s.schema_id = o.schema_id
                  join sys.types ty on ty.user_type_id = c.user_type_id
                  left join sys.default_constraints dc on dc.parent_object_id = c.object_id and dc.parent_column_id = c.column_id
                  left join sys.extended_properties ep on ep.major_id = c.object_id and ep.minor_id = c.column_id and ep.name = 'MS_Description'
                 where s.name = '{schema}'
                 order by o.name, c.column_id
                """,
            _ => string.Empty
        };
    }

    private static string BuildTableCommentSql(string providerName, string schemaName, string objectName, string objectType, string comment)
    {
        string normalizedType = NormalizeObjectType(objectType);
        return providerName switch
        {
            "Oracle" or "Dameng" or "PostgreSql" or "KingbaseES" => normalizedType switch
            {
                "view" => $"comment on view {QuoteAnsiQualifiedName(schemaName, objectName)} is {BuildAnsiCommentLiteral(providerName, comment)};",
                "materialized view" => $"comment on materialized view {QuoteAnsiQualifiedName(schemaName, objectName)} is {BuildAnsiCommentLiteral(providerName, comment)};",
                _ => $"comment on table {QuoteAnsiQualifiedName(schemaName, objectName)} is {BuildAnsiCommentLiteral(providerName, comment)};"
            },
            "MySql" when normalizedType == "table" => $"alter table {QuoteMySqlQualifiedName(schemaName, objectName)} comment = {QuoteSqlLiteral(comment)};",
            "SqlServer" when normalizedType is "table" or "view" => BuildSqlServerTableCommentSql(schemaName, objectName, normalizedType, comment),
            _ => string.Empty
        };
    }

    private static string BuildColumnCommentSql(string providerName, CommentMaintenanceColumnEntry column)
    {
        string normalizedType = NormalizeObjectType(column.ObjectType);
        return providerName switch
        {
            "Oracle" or "Dameng" or "PostgreSql" or "KingbaseES" =>
                $"comment on column {QuoteAnsiQualifiedName(column.SchemaName, column.TableName, column.ColumnName)} is {BuildAnsiCommentLiteral(providerName, column.EditedComment)};",
            "MySql" when normalizedType == "table" => BuildMySqlColumnCommentSql(column),
            "SqlServer" when normalizedType is "table" or "view" => BuildSqlServerColumnCommentSql(column),
            _ => string.Empty
        };
    }

    private static string BuildSqlServerTableCommentSql(string schemaName, string objectName, string objectType, string comment)
    {
        string level1Type = string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase) ? "VIEW" : "TABLE";
        string schemaLiteral = ToUnicodeSqlServerLiteral(schemaName);
        string objectLiteral = ToUnicodeSqlServerLiteral(objectName);
        string typeLiteral = level1Type == "VIEW" ? "'V'" : "'U'";
        if (string.IsNullOrWhiteSpace(comment))
        {
            return $"""
                if exists (
                    select 1
                      from sys.objects o
                      join sys.schemas s on s.schema_id = o.schema_id
                      join sys.extended_properties ep on ep.major_id = o.object_id and ep.minor_id = 0 and ep.name = N'MS_Description'
                     where s.name = {schemaLiteral}
                       and o.name = {objectLiteral}
                       and o.type = {typeLiteral}
                )
                    exec sys.sp_dropextendedproperty @name=N'MS_Description',
                        @level0type=N'SCHEMA', @level0name={schemaLiteral},
                        @level1type=N'{level1Type}', @level1name={objectLiteral};
                """;
        }

        string commentLiteral = ToUnicodeSqlServerLiteral(comment);
        return $"""
            if exists (
                select 1
                  from sys.objects o
                  join sys.schemas s on s.schema_id = o.schema_id
                  join sys.extended_properties ep on ep.major_id = o.object_id and ep.minor_id = 0 and ep.name = N'MS_Description'
                 where s.name = {schemaLiteral}
                   and o.name = {objectLiteral}
                   and o.type = {typeLiteral}
            )
                exec sys.sp_updateextendedproperty @name=N'MS_Description', @value={commentLiteral},
                    @level0type=N'SCHEMA', @level0name={schemaLiteral},
                    @level1type=N'{level1Type}', @level1name={objectLiteral};
            else
                exec sys.sp_addextendedproperty @name=N'MS_Description', @value={commentLiteral},
                    @level0type=N'SCHEMA', @level0name={schemaLiteral},
                    @level1type=N'{level1Type}', @level1name={objectLiteral};
            """;
    }

    private static string BuildSqlServerColumnCommentSql(CommentMaintenanceColumnEntry column)
    {
        string level1Type = string.Equals(NormalizeObjectType(column.ObjectType), "view", StringComparison.OrdinalIgnoreCase) ? "VIEW" : "TABLE";
        string schemaLiteral = ToUnicodeSqlServerLiteral(column.SchemaName);
        string tableLiteral = ToUnicodeSqlServerLiteral(column.TableName);
        string columnLiteral = ToUnicodeSqlServerLiteral(column.ColumnName);
        string typeLiteral = level1Type == "VIEW" ? "'V'" : "'U'";
        if (string.IsNullOrWhiteSpace(column.EditedComment))
        {
            return $"""
                if exists (
                    select 1
                      from sys.objects o
                      join sys.schemas s on s.schema_id = o.schema_id
                      join sys.columns c on c.object_id = o.object_id
                      join sys.extended_properties ep on ep.major_id = c.object_id and ep.minor_id = c.column_id and ep.name = N'MS_Description'
                     where s.name = {schemaLiteral}
                       and o.name = {tableLiteral}
                       and c.name = {columnLiteral}
                       and o.type = {typeLiteral}
                )
                    exec sys.sp_dropextendedproperty @name=N'MS_Description',
                        @level0type=N'SCHEMA', @level0name={schemaLiteral},
                        @level1type=N'{level1Type}', @level1name={tableLiteral},
                        @level2type=N'COLUMN', @level2name={columnLiteral};
                """;
        }

        string commentLiteral = ToUnicodeSqlServerLiteral(column.EditedComment);
        return $"""
            if exists (
                select 1
                  from sys.objects o
                  join sys.schemas s on s.schema_id = o.schema_id
                  join sys.columns c on c.object_id = o.object_id
                  join sys.extended_properties ep on ep.major_id = c.object_id and ep.minor_id = c.column_id and ep.name = N'MS_Description'
                 where s.name = {schemaLiteral}
                   and o.name = {tableLiteral}
                   and c.name = {columnLiteral}
                   and o.type = {typeLiteral}
            )
                exec sys.sp_updateextendedproperty @name=N'MS_Description', @value={commentLiteral},
                    @level0type=N'SCHEMA', @level0name={schemaLiteral},
                    @level1type=N'{level1Type}', @level1name={tableLiteral},
                    @level2type=N'COLUMN', @level2name={columnLiteral};
            else
                exec sys.sp_addextendedproperty @name=N'MS_Description', @value={commentLiteral},
                    @level0type=N'SCHEMA', @level0name={schemaLiteral},
                    @level1type=N'{level1Type}', @level1name={tableLiteral},
                    @level2type=N'COLUMN', @level2name={columnLiteral};
            """;
    }

    private static string BuildMySqlColumnCommentSql(CommentMaintenanceColumnEntry column)
    {
        if (column.IsComputed)
        {
            return string.Empty;
        }

        string fullType = string.IsNullOrWhiteSpace(column.FullTypeDefinition) ? column.DataType : column.FullTypeDefinition;
        if (string.IsNullOrWhiteSpace(fullType))
        {
            return string.Empty;
        }

        string nullableClause = column.IsNullable ? " null" : " not null";
        string defaultClause = BuildMySqlDefaultClause(column.DefaultValue);
        string extraClause = string.IsNullOrWhiteSpace(column.ExtraDefinition) ? string.Empty : " " + column.ExtraDefinition.Trim();
        string commentLiteral = QuoteSqlLiteral(column.EditedComment);

        return $"alter table {QuoteMySqlQualifiedName(column.SchemaName, column.TableName)} modify column {QuoteMySqlIdentifier(column.ColumnName)} {fullType}{nullableClause}{defaultClause}{extraClause} comment {commentLiteral};";
    }
    private static string BuildAnsiCommentLiteral(string providerName, string comment)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            return QuoteSqlLiteral(comment);
        }

        return providerName switch
        {
            "PostgreSql" or "KingbaseES" => "null",
            _ => "''"
        };
    }

    private static string BuildMySqlDefaultClause(string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return string.Empty;
        }

        string trimmed = defaultValue.Trim();
        if (string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return " default null";
        }

        if (IsSimpleSqlLiteral(trimmed))
        {
            return $" default {trimmed}";
        }

        return $" default {QuoteSqlLiteral(trimmed)}";
    }

    private static bool IsSimpleSqlLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (double.TryParse(value, out _))
        {
            return true;
        }

        return value.StartsWith("current_", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("now()", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("uuid()", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("(", StringComparison.Ordinal);
    }
    private static string ReadString(DbDataReader reader, string columnName, string defaultValue = "")
    {
        object value = reader[columnName];
        return value == DBNull.Value ? defaultValue : value?.ToString()?.Trim() ?? defaultValue;
    }
    private static bool ReadBoolean(DbDataReader reader, string columnName, bool defaultValue)
    {
        object value = reader[columnName];
        if (value == DBNull.Value)
        {
            return defaultValue;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        string text = value.ToString()?.Trim() ?? string.Empty;
        if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "n", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return defaultValue;
    }
    private static string BuildTableKey(string schemaName, string objectName, string objectType)
    {
        return string.Concat(schemaName.Trim(), "::", NormalizeObjectType(objectType), "::", objectName.Trim());
    }
    private static string BuildColumnKey(string schemaName, string tableName, string columnName)
    {
        return string.Concat(schemaName.Trim(), "::", tableName.Trim(), "::", columnName.Trim());
    }
    private static CommentChangeStatus ResolveStatus(string currentComment, string editedComment)
    {
        string current = currentComment?.Trim() ?? string.Empty;
        string edited = editedComment?.Trim() ?? string.Empty;
        if (string.Equals(current, edited, StringComparison.Ordinal))
        {
            return CommentChangeStatus.Unchanged;
        }

        if (string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(edited))
        {
            return CommentChangeStatus.Added;
        }

        if (!string.IsNullOrWhiteSpace(current) && string.IsNullOrWhiteSpace(edited))
        {
            return CommentChangeStatus.Cleared;
        }

        return CommentChangeStatus.Updated;
    }
    private static string BuildCsvRow(
        string schemaName,
        string targetType,
        string objectType,
        string objectName,
        string columnName,
        string currentComment,
        string editedComment,
        string dataType)
    {
        return string.Join(",",
            QuoteCsv(schemaName),
            QuoteCsv(targetType),
            QuoteCsv(objectType),
            QuoteCsv(objectName),
            QuoteCsv(columnName),
            QuoteCsv(currentComment),
            QuoteCsv(editedComment),
            QuoteCsv(dataType));
    }
    private static string QuoteCsv(string value)
    {
        string text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static string[] ParseCsvLine(string line)
    {
        List<string> values = [];
        StringBuilder builder = new();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];
            if (inQuotes)
            {
                if (current == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else if (current == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    builder.Append(current);
                }

                continue;
            }

            if (current == ',')
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            if (current == '"')
            {
                inQuotes = true;
                continue;
            }

            builder.Append(current);
        }

        values.Add(builder.ToString());
        return values.ToArray();
    }
    private static string NormalizeObjectType(string objectType)
    {
        string normalized = objectType?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "table" or "base table" or "user_table" => "table",
            "view" => "view",
            "materializedview" or "materialized view" => "materialized view",
            _ => normalized
        };
    }
    private static string QuoteAnsiQualifiedName(string schemaName, string objectName, string? columnName = null)
    {
        string schema = QuoteAnsiIdentifier(schemaName);
        string quotedObject = QuoteAnsiIdentifier(objectName);
        return columnName == null
            ? $"{schema}.{quotedObject}"
            : $"{schema}.{quotedObject}.{QuoteAnsiIdentifier(columnName)}";
    }
    private static string QuoteMySqlQualifiedName(string schemaName, string objectName)
    {
        return $"{QuoteMySqlIdentifier(schemaName)}.{QuoteMySqlIdentifier(objectName)}";
    }
    private static string QuoteAnsiIdentifier(string identifier)
    {
        return $"\"{(identifier ?? string.Empty).Replace("\"", "\"\"")}\"";
    }
    private static string QuoteMySqlIdentifier(string identifier)
    {
        return $"`{(identifier ?? string.Empty).Replace("`", "``")}`";
    }
    private static string QuoteSqlLiteral(string value)
    {
        return $"'{EscapeSqlLiteral(value)}'";
    }
    private static string ToUnicodeSqlServerLiteral(string value)
    {
        return $"N'{EscapeSqlLiteral(value)}'";
    }
    private static string EscapeSqlLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }
}
