using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;

public sealed class ObjectEditorService : IObjectEditorService
{
    private static readonly Regex SqlServerCreateRegex = new(
        @"^\s*create\s+(view|procedure|proc|function)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OracleCreateOrReplaceRegex = new(
        @"^\s*create\s+(or\s+replace\s+)?(view|procedure|function)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DbProviderRuntime _runtime;
    public ObjectEditorService(IDatabaseProviderCatalog providerCatalog)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
    }
    public async Task<ObjectEditorModel> LoadObjectEditorModelAsync(
        ConnectionProfile profile,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        string normalizedObjectType = NormalizeObjectType(objectType);
        string effectiveSchema = ResolveSchema(profile, schemaName);
        ObjectEditorCapability capability = ResolveCapability(provider.Name, normalizedObjectType);

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        string definition = await LoadDefinitionAsync(connection, provider.Name, effectiveSchema, objectName, normalizedObjectType, cancellationToken);
        string comment = await LoadCommentAsync(connection, provider.Name, effectiveSchema, objectName, normalizedObjectType, cancellationToken);
        IReadOnlyList<ObjectParameterDefinition> parameters = await LoadParametersAsync(connection, provider.Name, effectiveSchema, objectName, normalizedObjectType, cancellationToken);
        string returnType = await LoadReturnTypeAsync(connection, provider.Name, effectiveSchema, objectName, normalizedObjectType, cancellationToken);

        return new ObjectEditorModel
        {
            ProviderName = provider.Name,
            SchemaName = effectiveSchema,
            ObjectName = objectName,
            DisplayName = objectName,
            ObjectType = normalizedObjectType,
            CommentText = comment,
            ReturnType = returnType,
            OriginalDefinition = definition,
            Capability = capability,
            Parameters = parameters
        };
    }
    public Task<string> BuildPreviewSqlAsync(
        ConnectionProfile profile,
        ObjectEditorSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        return Task.FromResult(NormalizeDefinitionForExecution(provider.Name, request.ObjectType, request.Definition));
    }
    public async Task<ObjectEditorSaveResult> SaveObjectAsync(
        ConnectionProfile profile,
        ObjectEditorSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        string normalizedObjectType = NormalizeObjectType(request.ObjectType);
        ObjectEditorCapability capability = ResolveCapability(provider.Name, normalizedObjectType);
        if (capability != ObjectEditorCapability.Editable)
        {
            throw new InvalidOperationException("当前数据库类型下，该对象仅支持预览，不支持直接保存。");
        }

        string sql = NormalizeDefinitionForExecution(provider.Name, normalizedObjectType, request.Definition);
        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        IReadOnlyList<ObjectCompileMessage> compileMessages = await LoadCompileMessagesAsync(
            connection,
            provider.Name,
            ResolveSchema(profile, request.SchemaName),
            request.ObjectName,
            normalizedObjectType,
            cancellationToken);

        return new ObjectEditorSaveResult
        {
            Success = true,
            Message = compileMessages.Count == 0 ? "对象保存完成。" : "对象保存完成，但数据库返回了编译提示。",
            ExecutedSql = sql,
            CompileMessages = compileMessages
        };
    }
    public Task<IReadOnlyList<ObjectCompileMessage>> ValidateObjectAsync(
        ConnectionProfile profile,
        ObjectEditorSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        ObjectEditorCapability capability = ResolveCapability(provider.Name, request.ObjectType);
        if (capability == ObjectEditorCapability.Unsupported)
        {
            return Task.FromResult<IReadOnlyList<ObjectCompileMessage>>(
            [
                new ObjectCompileMessage
                {
                    Severity = "Info",
                    Message = "当前数据库类型暂不支持该对象的独立校验。"
                }
            ]);
        }

        if (string.IsNullOrWhiteSpace(request.Definition))
        {
            return Task.FromResult<IReadOnlyList<ObjectCompileMessage>>(
            [
                new ObjectCompileMessage
                {
                    Severity = "Warning",
                    Message = "当前对象定义为空，无法进行校验。"
                }
            ]);
        }

        string sql = NormalizeDefinitionForExecution(provider.Name, request.ObjectType, request.Definition);
        return Task.FromResult<IReadOnlyList<ObjectCompileMessage>>(
        [
            new ObjectCompileMessage
            {
                Severity = "Info",
                Message = capability == ObjectEditorCapability.PreviewOnly
                    ? "当前数据库类型下该对象仅支持预览，请确认 SQL 后手动执行。"
                    : $"当前对象可执行定义已生成，共 {sql.Length} 个字符。第一版暂未提供脱库语法校验，请以保存结果为准。"
            }
        ]);
    }
    private async Task<DbConnection> OpenConnectionAsync(DatabaseProviderDefinition provider, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        DbProviderFactory factory = _runtime.ResolveFactory(provider, profile);
        DbConnection connection = factory.CreateConnection()
            ?? throw new InvalidOperationException("Unable to create database connection.");

        connection.ConnectionString = _runtime.BuildConnectionString(provider, profile);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
    private async Task<string> LoadDefinitionAsync(
        DbConnection connection,
        string providerName,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        return providerName switch
        {
            "Oracle" or "Dameng" => await LoadOracleFamilyDefinitionAsync(connection, schemaName, objectName, objectType, cancellationToken),
            "SqlServer" => await LoadSqlServerDefinitionAsync(connection, schemaName, objectName, objectType, cancellationToken),
            "PostgreSql" or "KingbaseES" => await LoadPostgreSqlDefinitionAsync(connection, schemaName, objectName, objectType, cancellationToken),
            "MySql" => await LoadMySqlDefinitionAsync(connection, schemaName, objectName, objectType, cancellationToken),
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported by object editor.")
        };
    }
    private async Task<string> LoadOracleFamilyDefinitionAsync(
        DbConnection connection,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(schemaName.ToUpperInvariant());
        string escapedName = EscapeSqlLiteral(objectName.ToUpperInvariant());

        if (string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase))
        {
            string? ddl = await ExecuteScalarAsStringAsync(
                connection,
                $"select dbms_metadata.get_ddl('VIEW', '{escapedName}', '{escapedSchema}') from dual",
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(ddl))
            {
                return TrimTrailingSemicolons(ddl);
            }

            string? body = await ExecuteScalarAsStringAsync(
                connection,
                $"select text from all_views where owner = '{escapedSchema}' and view_name = '{escapedName}'",
                cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new InvalidOperationException($"未找到视图定义：{schemaName}.{objectName}");
            }

            return $"create or replace view {schemaName}.{objectName} as{Environment.NewLine}{body.Trim()}";
        }

        string sourceType = string.Equals(objectType, "function", StringComparison.OrdinalIgnoreCase) ? "FUNCTION" : "PROCEDURE";
        string sql =
            $"select text from all_source where owner = '{escapedSchema}' and name = '{escapedName}' and type = '{sourceType}' order by line";
        string sourceText = await ReadLinesAsync(connection, sql, cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            throw new InvalidOperationException("未找到对象源码。");
        }

        return EnsureOracleCreateOrReplace(objectType, sourceText);
    }
    private async Task<string> LoadSqlServerDefinitionAsync(
        DbConnection connection,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(schemaName);
        string escapedName = EscapeSqlLiteral(objectName);
        string typeFilter = objectType switch
        {
            "view" => "'V'",
            "procedure" => "'P'",
            "function" => "'FN','IF','TF'",
            _ => "'V'"
        };

        string sql = $"""
            select m.definition
            from sys.sql_modules m
            inner join sys.objects o on o.object_id = m.object_id
            inner join sys.schemas s on s.schema_id = o.schema_id
            where s.name = '{escapedSchema}'
              and o.name = '{escapedName}'
              and o.type in ({typeFilter})
            """;
        string? definition = await ExecuteScalarAsStringAsync(connection, sql, cancellationToken);
        if (string.IsNullOrWhiteSpace(definition))
        {
            throw new InvalidOperationException("未找到对象定义。");
        }

        return definition.Trim();
    }
    private async Task<string> LoadPostgreSqlDefinitionAsync(
        DbConnection connection,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(schemaName);
        string escapedName = EscapeSqlLiteral(objectName);

        if (string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase))
        {
            string? definition = await ExecuteScalarAsStringAsync(
                connection,
                $"select definition from pg_views where schemaname = '{escapedSchema}' and viewname = '{escapedName}'",
                cancellationToken);
            if (string.IsNullOrWhiteSpace(definition))
            {
                throw new InvalidOperationException("未找到视图定义。");
            }

            return $"create or replace view {QuoteCompositeName("PostgreSql", schemaName, objectName)} as{Environment.NewLine}{definition.Trim()}";
        }

        string sql = $"""
            select pg_get_functiondef(p.oid)
            from pg_proc p
            inner join pg_namespace n on n.oid = p.pronamespace
            where n.nspname = '{escapedSchema}'
              and p.proname = '{escapedName}'
              and p.prokind = '{(string.Equals(objectType, "procedure", StringComparison.OrdinalIgnoreCase) ? "p" : "f")}'
            order by p.oid
            limit 1
            """;
        string? source = await ExecuteScalarAsStringAsync(connection, sql, cancellationToken);
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException("未找到对象源码。");
        }

        return source.Trim();
    }
    private async Task<string> LoadMySqlDefinitionAsync(
        DbConnection connection,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        string qualifiedName = $"`{schemaName.Replace("`", "``", StringComparison.Ordinal)}`.`{objectName.Replace("`", "``", StringComparison.Ordinal)}`";
        string sql = objectType switch
        {
            "view" => $"show create view {qualifiedName}",
            "procedure" => $"show create procedure {qualifiedName}",
            "function" => $"show create function {qualifiedName}",
            _ => $"show create view {qualifiedName}"
        };

        string? definition = await ReadShowCreateDefinitionAsync(connection, sql, cancellationToken);
        if (string.IsNullOrWhiteSpace(definition))
        {
            throw new InvalidOperationException("未找到对象定义。");
        }

        return definition.Trim();
    }
    private async Task<string> LoadCommentAsync(
        DbConnection connection,
        string providerName,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(providerName is "Oracle" or "Dameng" ? schemaName.ToUpperInvariant() : schemaName);
        string escapedName = EscapeSqlLiteral(providerName is "Oracle" or "Dameng" ? objectName.ToUpperInvariant() : objectName);

        return providerName switch
        {
            "Oracle" or "Dameng" when string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase) =>
                (await ExecuteScalarAsStringAsync(
                    connection,
                    $"select comments from all_tab_comments where owner = '{escapedSchema}' and table_name = '{escapedName}'",
                    cancellationToken)) ?? string.Empty,
            "SqlServer" =>
                (await ExecuteScalarAsStringAsync(
                    connection,
                    $"""
                    select cast(ep.value as nvarchar(max))
                    from sys.objects o
                    inner join sys.schemas s on s.schema_id = o.schema_id
                    left join sys.extended_properties ep on ep.major_id = o.object_id and ep.minor_id = 0 and ep.name = 'MS_Description'
                    where s.name = '{escapedSchema}' and o.name = '{escapedName}'
                    """,
                    cancellationToken)) ?? string.Empty,
            "PostgreSql" or "KingbaseES" when string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase) =>
                (await ExecuteScalarAsStringAsync(
                    connection,
                    $"""
                    select obj_description(c.oid)
                    from pg_class c
                    inner join pg_namespace n on n.oid = c.relnamespace
                    where n.nspname = '{escapedSchema}' and c.relname = '{escapedName}'
                    """,
                    cancellationToken)) ?? string.Empty,
            "MySql" when string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase) =>
                (await ExecuteScalarAsStringAsync(
                    connection,
                    $"select table_comment from information_schema.tables where table_schema = '{escapedSchema}' and table_name = '{escapedName}'",
                    cancellationToken)) ?? string.Empty,
            _ => string.Empty
        };
    }
    private async Task<IReadOnlyList<ObjectParameterDefinition>> LoadParametersAsync(
        DbConnection connection,
        string providerName,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        if (string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ObjectParameterDefinition>();
        }

        string escapedSchema = EscapeSqlLiteral(providerName is "Oracle" or "Dameng" ? schemaName.ToUpperInvariant() : schemaName);
        string escapedName = EscapeSqlLiteral(providerName is "Oracle" or "Dameng" ? objectName.ToUpperInvariant() : objectName);
        List<ObjectParameterDefinition> items = [];

        string sql = providerName switch
        {
            "Oracle" or "Dameng" => $"""
                select argument_name, data_type, in_out, defaulted
                from all_arguments
                where owner = '{escapedSchema}'
                  and object_name = '{escapedName}'
                  and argument_name is not null
                order by position
                """,
            "SqlServer" => $"""
                select p.name, t.name as data_type,
                       case when p.is_output = 1 then 'OUT' else 'IN' end as direction,
                       '' as defaulted
                from sys.parameters p
                inner join sys.objects o on o.object_id = p.object_id
                inner join sys.schemas s on s.schema_id = o.schema_id
                inner join sys.types t on t.user_type_id = p.user_type_id
                where s.name = '{escapedSchema}' and o.name = '{escapedName}'
                order by p.parameter_id
                """,
            "PostgreSql" or "KingbaseES" => $"""
                select parameter_name, data_type, parameter_mode, parameter_default
                from information_schema.parameters
                where specific_schema = '{escapedSchema}'
                  and specific_name like '{escapedName}%'
                order by ordinal_position
                """,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<ObjectParameterDefinition>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ObjectParameterDefinition
            {
                Name = Convert.ToString(reader.GetValue(0)) ?? string.Empty,
                DataType = reader.FieldCount > 1 ? Convert.ToString(reader.GetValue(1)) ?? string.Empty : string.Empty,
                Direction = reader.FieldCount > 2 ? Convert.ToString(reader.GetValue(2)) ?? string.Empty : string.Empty,
                DefaultValue = reader.FieldCount > 3 ? Convert.ToString(reader.GetValue(3)) ?? string.Empty : string.Empty
            });
        }

        return items;
    }
    private async Task<string> LoadReturnTypeAsync(
        DbConnection connection,
        string providerName,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(objectType, "function", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string escapedSchema = EscapeSqlLiteral(providerName is "Oracle" or "Dameng" ? schemaName.ToUpperInvariant() : schemaName);
        string escapedName = EscapeSqlLiteral(providerName is "Oracle" or "Dameng" ? objectName.ToUpperInvariant() : objectName);
        string sql = providerName switch
        {
            "Oracle" or "Dameng" => $"""
                select data_type
                from all_arguments
                where owner = '{escapedSchema}'
                  and object_name = '{escapedName}'
                  and position = 0
                """,
            "PostgreSql" or "KingbaseES" => $"""
                select pg_catalog.format_type(p.prorettype, null)
                from pg_proc p
                inner join pg_namespace n on n.oid = p.pronamespace
                where n.nspname = '{escapedSchema}' and p.proname = '{escapedName}'
                order by p.oid
                limit 1
                """,
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(sql)
            ? string.Empty
            : (await ExecuteScalarAsStringAsync(connection, sql, cancellationToken)) ?? string.Empty;
    }
    private async Task<IReadOnlyList<ObjectCompileMessage>> LoadCompileMessagesAsync(
        DbConnection connection,
        string providerName,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        if (providerName is not ("Oracle" or "Dameng"))
        {
            return Array.Empty<ObjectCompileMessage>();
        }

        string sourceType = NormalizeObjectType(objectType).ToUpperInvariant() switch
        {
            "PROCEDURE" => "PROCEDURE",
            "FUNCTION" => "FUNCTION",
            "VIEW" => "VIEW",
            _ => "VIEW"
        };
        string sql = $"""
            select line, position, attribute, text
            from all_errors
            where owner = '{EscapeSqlLiteral(schemaName.ToUpperInvariant())}'
              and name = '{EscapeSqlLiteral(objectName.ToUpperInvariant())}'
              and type = '{sourceType}'
            order by sequence
            """;

        List<ObjectCompileMessage> items = [];
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ObjectCompileMessage
            {
                Line = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                Column = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                Severity = reader.IsDBNull(2) ? "Error" : Convert.ToString(reader.GetValue(2)) ?? "Error",
                Message = reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3)) ?? string.Empty
            });
        }

        return items;
    }
    private static async Task<string?> ReadShowCreateDefinitionAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        for (int index = 0; index < reader.FieldCount; index++)
        {
            string name = reader.GetName(index);
            if (name.Contains("Create", StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index));
            }
        }

        return reader.FieldCount > 1 && !reader.IsDBNull(1)
            ? Convert.ToString(reader.GetValue(1))
            : null;
    }
    private static async Task<string?> ExecuteScalarAsStringAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        if (value == null || value == DBNull.Value)
        {
            return null;
        }

        return Convert.ToString(value);
    }
    private static async Task<string> ReadLinesAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        StringBuilder builder = new();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string line = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0)) ?? string.Empty;
            builder.Append(line);
        }

        return builder.ToString().Trim();
    }
    private static string NormalizeDefinitionForExecution(string providerName, string objectType, string definition)
    {
        string normalizedObjectType = NormalizeObjectType(objectType);
        string sql = TrimTrailingSemicolons(definition);
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException("对象定义不能为空。");
        }

        return providerName switch
        {
            "Oracle" or "Dameng" => EnsureOracleCreateOrReplace(normalizedObjectType, sql),
            "SqlServer" => EnsureSqlServerCreateOrAlter(sql),
            "MySql" when string.Equals(normalizedObjectType, "view", StringComparison.OrdinalIgnoreCase) => EnsureMySqlViewCreateOrReplace(sql),
            _ => sql
        };
    }
    private static string EnsureOracleCreateOrReplace(string objectType, string definition)
    {
        string trimmed = TrimTrailingSemicolons(definition);
        if (trimmed.StartsWith("create or replace", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("create ", StringComparison.OrdinalIgnoreCase))
        {
            return "create or replace" + trimmed["create".Length..];
        }

        if (OracleCreateOrReplaceRegex.IsMatch(trimmed))
        {
            return OracleCreateOrReplaceRegex.Replace(trimmed, match =>
            {
                string keyword = NormalizeObjectType(match.Groups[2].Value);
                return $"create or replace {keyword}";
            });
        }

        string normalizedObjectType = NormalizeObjectType(objectType);
        if (Regex.IsMatch(trimmed, $@"^\s*{Regex.Escape(normalizedObjectType)}\b", RegexOptions.IgnoreCase))
        {
            return $"create or replace {trimmed.TrimStart()}";
        }

        return $"create or replace {normalizedObjectType} {trimmed.TrimStart()}";
    }
    private static string EnsureSqlServerCreateOrAlter(string definition)
    {
        string trimmed = TrimTrailingSemicolons(definition);
        if (trimmed.StartsWith("create or alter", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return SqlServerCreateRegex.Replace(trimmed, match =>
        {
            string typeKeyword = match.Groups[1].Value;
            if (string.Equals(typeKeyword, "proc", StringComparison.OrdinalIgnoreCase))
            {
                typeKeyword = "procedure";
            }

            return $"create or alter {typeKeyword}";
        });
    }
    private static string EnsureMySqlViewCreateOrReplace(string definition)
    {
        string trimmed = TrimTrailingSemicolons(definition);
        if (trimmed.StartsWith("create or replace view", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("create view", StringComparison.OrdinalIgnoreCase))
        {
            return "create or replace view" + trimmed["create view".Length..];
        }

        return trimmed;
    }
    private static string TrimTrailingSemicolons(string sql)
    {
        string value = (sql ?? string.Empty).Trim();
        while (value.EndsWith(';'))
        {
            value = value[..^1].TrimEnd();
        }

        return value;
    }
    private static ObjectEditorCapability ResolveCapability(string providerName, string objectType)
    {
        string normalizedObjectType = NormalizeObjectType(objectType);
        return providerName switch
        {
            "Oracle" or "Dameng" when normalizedObjectType is "view" or "procedure" or "function" => ObjectEditorCapability.Editable,
            "SqlServer" when normalizedObjectType is "view" or "procedure" or "function" => ObjectEditorCapability.Editable,
            "PostgreSql" or "KingbaseES" when normalizedObjectType is "view" or "function" => ObjectEditorCapability.Editable,
            "PostgreSql" or "KingbaseES" when normalizedObjectType == "procedure" => ObjectEditorCapability.PreviewOnly,
            "MySql" when normalizedObjectType == "view" => ObjectEditorCapability.Editable,
            "MySql" when normalizedObjectType is "procedure" or "function" => ObjectEditorCapability.PreviewOnly,
            _ => ObjectEditorCapability.Unsupported
        };
    }
    private static string NormalizeObjectType(string? objectType)
    {
        return (objectType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "proc" => "procedure",
            "storedprocedure" => "procedure",
            _ => (objectType ?? string.Empty).Trim().ToLowerInvariant()
        };
    }
    private static string ResolveSchema(ConnectionProfile profile, string schemaName)
    {
        if (!string.IsNullOrWhiteSpace(schemaName))
        {
            return schemaName;
        }

        if (!string.IsNullOrWhiteSpace(profile.Schema))
        {
            return profile.Schema;
        }

        if (!string.IsNullOrWhiteSpace(profile.UserName))
        {
            return profile.UserName;
        }

        return string.Empty;
    }
    private static string QuoteCompositeName(string providerName, string schemaName, string objectName)
    {
        return providerName switch
        {
            "SqlServer" => $"[{schemaName}].[{objectName}]",
            "MySql" => $"`{schemaName}`.`{objectName}`",
            _ => $"\"{schemaName}\".\"{objectName}\""
        };
    }
    private static string EscapeSqlLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }
}
