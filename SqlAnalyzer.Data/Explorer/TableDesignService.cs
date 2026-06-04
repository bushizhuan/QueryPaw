using System.Data.Common;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;

public sealed class TableDesignService : ITableDesignService
{
    private readonly DbProviderRuntime _runtime;
    public TableDesignService(IDatabaseProviderCatalog providerCatalog)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
    }

    // 表设计采用“分区容错”加载，某一块元数据失败时不影响字段等核心信息显示，避免整个设计器空白。
    public async Task<TableDesignModel> LoadTableDesignAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        string effectiveSchema = ResolveEffectiveSchema(provider, profile, schemaName);
        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);

        string tableComment = await SafeLoadValueAsync(
            () => LoadTableCommentAsync(connection, provider, effectiveSchema, tableName, cancellationToken),
            string.Empty);
        List<TableColumnDefinition> columns = await SafeLoadListAsync(
            () => LoadTableColumnsAsync(connection, provider, effectiveSchema, tableName, cancellationToken));
        List<TableIndexDefinition> indexes = await SafeLoadListAsync(
            () => LoadTableIndexesAsync(connection, provider, effectiveSchema, tableName, cancellationToken));
        List<TableIndexDefinition> uniqueKeys = await SafeLoadListAsync(
            () => LoadTableUniqueKeysAsync(connection, provider, effectiveSchema, tableName, cancellationToken));
        List<TableForeignKeyDefinition> foreignKeys = await SafeLoadListAsync(
            () => LoadTableForeignKeysAsync(connection, provider, effectiveSchema, tableName, cancellationToken));
        List<TableCheckDefinition> checks = await SafeLoadListAsync(
            () => LoadTableChecksAsync(connection, provider, effectiveSchema, tableName, cancellationToken));
        List<TableTriggerDefinition> triggers = await SafeLoadListAsync(
            () => LoadTableTriggersAsync(connection, provider, effectiveSchema, tableName, cancellationToken));
        List<TableOptionDefinition> options = await SafeLoadListAsync(
            () => LoadTableOptionsAsync(connection, provider, effectiveSchema, tableName, cancellationToken));

        return new TableDesignModel
        {
            ProviderName = provider.Name,
            SchemaName = effectiveSchema,
            TableName = tableName,
            TableComment = tableComment,
            SupportsDirectSave = provider.Capabilities.SupportsDirectTableAlter,
            CapabilityLevel = provider.Capabilities.SupportsDirectTableAlter ? "DirectSave" : "PreviewOnly",
            Columns = columns,
            Indexes = indexes,
            UniqueKeys = uniqueKeys,
            ForeignKeys = foreignKeys,
            Checks = checks,
            Triggers = triggers,
            Options = options
        };
    }
    public async Task<string> ExportTableStructureAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        string effectiveSchema = ResolveEffectiveSchema(provider, profile, schemaName);
        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);

        if (string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider.Name, "Dameng", StringComparison.OrdinalIgnoreCase))
        {
            string sql =
                $"select DBMS_METADATA.GET_DDL('TABLE', '{EscapeSqlLiteral(tableName.ToUpperInvariant())}', '{EscapeSqlLiteral(effectiveSchema.ToUpperInvariant())}') from dual";
            string ddl = await ReadSingleScalarAsync(connection, sql, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ddl))
            {
                return ddl;
            }
        }

        TableDesignModel design = await LoadTableDesignAsync(profile, effectiveSchema, tableName, cancellationToken);
        return BuildCreateTableScript(design);
    }
    public async Task<string> ExportTableDataAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        string effectiveSchema = ResolveEffectiveSchema(provider, profile, schemaName);
        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        await using DbCommand command = connection.CreateCommand();
        string qualifiedName = BuildQualifiedName(provider, effectiveSchema, tableName);
        command.CommandText = $"select * from {qualifiedName}";
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<string> rows = [];
        string[] columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        string insertColumns = string.Join(", ", columns.Select(column => QuoteIdentifier(provider.Name, column)));
        while (await reader.ReadAsync(cancellationToken))
        {
            string values = string.Join(", ", columns.Select((_, index) => FormatSqlLiteral(provider, reader.GetValue(index))));
            rows.Add($"insert into {qualifiedName} ({insertColumns}) values ({values});");
        }

        return string.Join(Environment.NewLine, rows);
    }
    public async Task SaveTableDesignAsync(ConnectionProfile profile, TableDesignModel originalDesign, TableDesignModel updatedDesign, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (!string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(provider.Name, "Dameng", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Table structure save is currently supported for Oracle-family connections.");
        }

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        string tableName = BuildQualifiedName(provider, updatedDesign.SchemaName, updatedDesign.TableName);
        Dictionary<string, TableColumnDefinition> originalColumns = originalDesign.Columns.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TableColumnDefinition> updatedColumns = updatedDesign.Columns.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        foreach ((string columnName, TableColumnDefinition column) in updatedColumns)
        {
            if (!originalColumns.TryGetValue(columnName, out TableColumnDefinition? original))
            {
                await ExecuteNonQueryAsync(connection, $"alter table {tableName} add ({BuildColumnDefinitionSql(provider.Name, column)})", cancellationToken);
                await ApplyColumnCommentAsync(connection, updatedDesign, column, cancellationToken);
                continue;
            }

            if (HasColumnDefinitionChanged(original, column))
            {
                await ExecuteNonQueryAsync(connection, $"alter table {tableName} modify ({BuildColumnDefinitionSql(provider.Name, column)})", cancellationToken);
            }

            if (!string.Equals(original.Comment ?? string.Empty, column.Comment ?? string.Empty, StringComparison.Ordinal))
            {
                await ApplyColumnCommentAsync(connection, updatedDesign, column, cancellationToken);
            }
        }

        foreach ((string columnName, TableColumnDefinition original) in originalColumns)
        {
            if (!updatedColumns.ContainsKey(columnName))
            {
                await ExecuteNonQueryAsync(connection, $"alter table {tableName} drop column {QuoteIdentifier(provider.Name, original.Name)}", cancellationToken);
            }
        }

        await SynchronizePrimaryKeyAsync(connection, updatedDesign, originalDesign, cancellationToken);
    }
    private async Task<DbConnection> OpenConnectionAsync(DatabaseProviderDefinition provider, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        DbProviderFactory factory = _runtime.ResolveFactory(provider, profile);
        DbConnection connection = factory.CreateConnection() ?? throw new InvalidOperationException($"Cannot create connection for provider '{provider.Name}'.");
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
            "MySql" or "MariaDB" => profile.Database?.Trim() ?? string.Empty,
            "PostgreSql" or "KingbaseES" => "public",
            "Oracle" or "Dameng" => profile.UserName?.Trim() ?? string.Empty,
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
    private static bool ParseNullableFlag(object? value)
    {
        string? text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return text.Equals("Y", StringComparison.OrdinalIgnoreCase)
            || text.Equals("YES", StringComparison.OrdinalIgnoreCase)
            || text.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    }
    private static int? ReadNullableInt(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }
    private static async Task<List<T>> SafeLoadListAsync<T>(Func<Task<IReadOnlyList<T>>> loader)
    {
        try
        {
            IReadOnlyList<T> items = await loader();
            return items?.ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
    private static async Task<T> SafeLoadValueAsync<T>(Func<Task<T>> loader, T fallback)
    {
        try
        {
            return await loader();
        }
        catch
        {
            return fallback;
        }
    }
    private static async Task<string> ReadSingleScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar?.ToString() ?? string.Empty;
    }
    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // 字段列表是表设计最核心的数据来源，这里优先保证跨库字段类型、长度、注释和主键标记能稳定读出。
    private static async Task<IReadOnlyList<TableColumnDefinition>> LoadTableColumnsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select column_name,
       data_type,
       data_length,
       data_precision,
       data_scale,
       nullable,
       column_id
  from all_tab_columns
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by column_id",
            "Dameng" => $@"
select column_name,
       data_type,
       data_length,
       data_precision,
       data_scale,
       nullable,
       column_id
  from all_tab_columns
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by column_id",
            "SqlServer" => $@"
select column_name,
       data_type,
       case when character_maximum_length is null or character_maximum_length < 0 then null else character_maximum_length end as data_length,
       numeric_precision,
       numeric_scale,
       is_nullable,
       ordinal_position
  from information_schema.columns
 where table_schema = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by ordinal_position",
            "PostgreSql" => $@"
select column_name,
       data_type,
       character_maximum_length,
       numeric_precision,
       numeric_scale,
       is_nullable,
       ordinal_position
  from information_schema.columns
 where table_schema = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by ordinal_position",
            "KingbaseES" => $@"
select column_name,
       data_type,
       character_maximum_length,
       numeric_precision,
       numeric_scale,
       is_nullable,
       ordinal_position
  from information_schema.columns
 where table_schema = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by ordinal_position",
            "MySql" or "MariaDB" => $@"
select column_name,
       data_type,
       character_maximum_length,
       numeric_precision,
       numeric_scale,
       is_nullable,
       ordinal_position
  from information_schema.columns
 where table_schema = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by ordinal_position",
            "SQLite" => $@"
select name,
       type,
       null,
       null,
       null,
       case when [notnull] = 0 then 'YES' else 'NO' end,
       cid + 1
  from pragma_table_info('{escapedTable}')
 order by cid",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<TableColumnDefinition>();
        }

        Dictionary<string, string> comments = await LoadColumnCommentsAsync(connection, provider, schemaName, tableName, cancellationToken);
        HashSet<string> primaryKeys = await LoadPrimaryKeyColumnsAsync(connection, provider, schemaName, tableName, cancellationToken);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<TableColumnDefinition> columns = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader.GetString(0);
            columns.Add(new TableColumnDefinition
            {
                Name = columnName,
                DataType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Length = ReadNullableInt(reader, 2),
                Precision = ReadNullableInt(reader, 3),
                Scale = ReadNullableInt(reader, 4),
                IsNullable = ParseNullableFlag(reader.IsDBNull(5) ? null : reader.GetValue(5)),
                IsPrimaryKey = primaryKeys.Contains(columnName),
                Comment = comments.TryGetValue(columnName, out string? comment) ? comment : string.Empty
            });
        }

        return columns;
    }
    private static async Task<Dictionary<string, string>> LoadColumnCommentsAsync(DbConnection connection, DatabaseProviderDefinition provider, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select column_name, comments
  from all_col_comments
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'",
            "Dameng" => $@"
select column_name, comments
  from all_col_comments
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'",
            "SqlServer" => $@"
select c.name as column_name,
       cast(ep.value as nvarchar(max)) as comments
  from sys.columns c
  join sys.tables t
    on c.object_id = t.object_id
  join sys.schemas s
    on t.schema_id = s.schema_id
  left join sys.extended_properties ep
    on ep.major_id = c.object_id
   and ep.minor_id = c.column_id
   and ep.name = 'MS_Description'
 where s.name = '{escapedSchema}'
   and t.name = '{escapedTable}'
 order by c.column_id",
            "PostgreSql" => $@"
select a.attname as column_name,
       coalesce(pg_catalog.col_description(c.oid, a.attnum), '') as comments
  from pg_catalog.pg_class c
  join pg_catalog.pg_namespace n
    on n.oid = c.relnamespace
  join pg_catalog.pg_attribute a
    on a.attrelid = c.oid
   and a.attnum > 0
   and not a.attisdropped
 where n.nspname = '{escapedSchema}'
   and c.relname = '{escapedTable}'
 order by a.attnum",
            "KingbaseES" => $@"
select a.attname as column_name,
       coalesce(pg_catalog.col_description(c.oid, a.attnum), '') as comments
  from pg_catalog.pg_class c
  join pg_catalog.pg_namespace n
    on n.oid = c.relnamespace
  join pg_catalog.pg_attribute a
    on a.attrelid = c.oid
   and a.attnum > 0
   and not a.attisdropped
 where n.nspname = '{escapedSchema}'
   and c.relname = '{escapedTable}'
 order by a.attnum",
            "MySql" or "MariaDB" => $@"
select column_name, column_comment
  from information_schema.columns
 where table_schema = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by ordinal_position",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                comments[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            }
        }

        return comments;
    }
    private static async Task<HashSet<string>> LoadPrimaryKeyColumnsAsync(DbConnection connection, DatabaseProviderDefinition provider, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
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
            "SQLite" => $@"
select name
  from pragma_table_info('{escapedTable}')
 where pk > 0
 order by pk",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                keys.Add(reader.GetString(0));
            }
        }

        return keys;
    }

    // 索引读取和字段读取分离，索引失败时不会把字段区域一起拖成空白。
    private static async Task<IReadOnlyList<TableIndexDefinition>> LoadTableIndexesAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select aic.index_name,
       ai.uniqueness,
       aic.column_name,
       aic.column_position
  from all_ind_columns aic
  join all_indexes ai
    on aic.index_owner = ai.owner
   and aic.index_name = ai.index_name
 where aic.index_owner = '{escapedSchema}'
   and aic.table_name = '{escapedTable}'
 order by aic.index_name, aic.column_position",
            "Dameng" => $@"
select aic.index_name,
       ai.uniqueness,
       aic.column_name,
       aic.column_position
  from all_ind_columns aic
  join all_indexes ai
    on aic.index_owner = ai.owner
   and aic.index_name = ai.index_name
 where aic.index_owner = '{escapedSchema}'
   and aic.table_name = '{escapedTable}'
 order by aic.index_name, aic.column_position",
            "SQLite" => $@"
select il.name as index_name,
       case when il.""unique"" = 1 then 'UNIQUE' else '' end as uniqueness,
       ii.name as column_name,
       ii.seqno + 1 as column_position
  from pragma_index_list('{escapedTable}') il
  join pragma_index_info(il.name) ii
 order by il.name, ii.seqno",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<TableIndexDefinition>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        Dictionary<string, TableIndexDefinition> indexes = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            string name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!indexes.TryGetValue(name, out TableIndexDefinition? index))
            {
                index = new TableIndexDefinition
                {
                    Name = name,
                    IsUnique = !reader.IsDBNull(1) && string.Equals(reader.GetString(1), "UNIQUE", StringComparison.OrdinalIgnoreCase),
                    Columns = string.Empty
                };
                indexes[name] = index;
            }

            if (!reader.IsDBNull(2))
            {
                index.Columns = string.IsNullOrWhiteSpace(index.Columns) ? reader.GetString(2) : $"{index.Columns}, {reader.GetString(2)}";
            }
        }

        return indexes.Values.ToArray();
    }
    private static async Task<IReadOnlyList<TableIndexDefinition>> LoadTableUniqueKeysAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select acc.constraint_name,
       acc.column_name,
       acc.position
  from all_constraints ac
  join all_cons_columns acc
    on ac.owner = acc.owner
   and ac.constraint_name = acc.constraint_name
 where ac.constraint_type = 'U'
   and ac.owner = '{escapedSchema}'
   and ac.table_name = '{escapedTable}'
 order by acc.constraint_name, acc.position",
            "Dameng" => $@"
select acc.constraint_name,
       acc.column_name,
       acc.position
  from all_constraints ac
  join all_cons_columns acc
    on ac.owner = acc.owner
   and ac.constraint_name = acc.constraint_name
 where ac.constraint_type = 'U'
   and ac.owner = '{escapedSchema}'
   and ac.table_name = '{escapedTable}'
 order by acc.constraint_name, acc.position",
            "SQLite" => $@"
select il.name as constraint_name,
       ii.name as column_name,
       ii.seqno + 1 as position
  from pragma_index_list('{escapedTable}') il
  join pragma_index_info(il.name) ii
 where il.""unique"" = 1
 order by il.name, ii.seqno",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<TableIndexDefinition>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        Dictionary<string, TableIndexDefinition> keys = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            string name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!keys.TryGetValue(name, out TableIndexDefinition? key))
            {
                key = new TableIndexDefinition
                {
                    Name = name,
                    IsUnique = true,
                    Columns = string.Empty
                };
                keys[name] = key;
            }

            if (!reader.IsDBNull(1))
            {
                key.Columns = string.IsNullOrWhiteSpace(key.Columns) ? reader.GetString(1) : $"{key.Columns}, {reader.GetString(1)}";
            }
        }

        return keys.Values.ToArray();
    }
    private static async Task<IReadOnlyList<TableForeignKeyDefinition>> LoadTableForeignKeysAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select acc.constraint_name,
       acc.column_name,
       rc.owner,
       rc.table_name,
       rcc.column_name,
       acc.position
  from all_constraints ac
  join all_cons_columns acc
    on ac.owner = acc.owner
   and ac.constraint_name = acc.constraint_name
 join all_constraints rc
    on ac.r_owner = rc.owner
   and ac.r_constraint_name = rc.constraint_name
  join all_cons_columns rcc
    on rc.owner = rcc.owner
   and rc.constraint_name = rcc.constraint_name
   and acc.position = rcc.position
 where ac.constraint_type = 'R'
   and ac.owner = '{escapedSchema}'
   and ac.table_name = '{escapedTable}'
 order by acc.constraint_name, acc.position",
            "Dameng" => $@"
select acc.constraint_name,
       acc.column_name,
       rc.owner,
       rc.table_name,
       rcc.column_name,
       acc.position
  from all_constraints ac
  join all_cons_columns acc
    on ac.owner = acc.owner
   and ac.constraint_name = acc.constraint_name
  join all_constraints rc
    on ac.r_owner = rc.owner
   and ac.r_constraint_name = rc.constraint_name
  join all_cons_columns rcc
    on rc.owner = rcc.owner
   and rc.constraint_name = rcc.constraint_name
   and acc.position = rcc.position
 where ac.constraint_type = 'R'
   and ac.owner = '{escapedSchema}'
   and ac.table_name = '{escapedTable}'
 order by acc.constraint_name, acc.position",
            "SQLite" => $@"
select 'FK_' || id as constraint_name,
       ""from"" as column_name,
       '' as referenced_owner,
       ""table"" as referenced_table,
       ""to"" as referenced_column,
       seq + 1 as position
  from pragma_foreign_key_list('{escapedTable}')
 order by id, seq",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<TableForeignKeyDefinition>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        Dictionary<string, TableForeignKeyDefinition> keys = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            string name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!keys.TryGetValue(name, out TableForeignKeyDefinition? key))
            {
                key = new TableForeignKeyDefinition
                {
                    Name = name,
                    Columns = string.Empty,
                    ReferenceColumns = string.Empty,
                    ReferenceTable = string.Empty
                };
                keys[name] = key;
            }

            if (!reader.IsDBNull(1))
            {
                key.Columns = string.IsNullOrWhiteSpace(key.Columns) ? reader.GetString(1) : $"{key.Columns}, {reader.GetString(1)}";
            }

            string referencedOwner = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            string referencedTable = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            key.ReferenceTable = string.IsNullOrWhiteSpace(referencedOwner)
                ? referencedTable
                : $"{referencedOwner}.{referencedTable}";

            if (!reader.IsDBNull(4))
            {
                key.ReferenceColumns = string.IsNullOrWhiteSpace(key.ReferenceColumns) ? reader.GetString(4) : $"{key.ReferenceColumns}, {reader.GetString(4)}";
            }
        }

        return keys.Values.ToArray();
    }
    private static async Task<IReadOnlyList<TableCheckDefinition>> LoadTableChecksAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select constraint_name, search_condition
  from all_constraints
 where constraint_type = 'C'
   and owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by constraint_name",
            "Dameng" => $@"
select constraint_name, search_condition
  from all_constraints
 where constraint_type = 'C'
   and owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by constraint_name",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<TableCheckDefinition>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<TableCheckDefinition> checks = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            checks.Add(new TableCheckDefinition
            {
                Name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Expression = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
            });
        }

        return checks;
    }
    private static async Task<IReadOnlyList<TableTriggerDefinition>> LoadTableTriggersAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select trigger_name, trigger_type, triggering_event, status, description
  from all_triggers
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by trigger_name",
            "Dameng" => $@"
select trigger_name, trigger_type, triggering_event, status, description
  from all_triggers
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
 order by trigger_name",
            "SQLite" => $@"
select name, '', '', '', coalesce(sql, '')
  from sqlite_master
 where type = 'trigger'
   and tbl_name = '{escapedTable}'
 order by name",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<TableTriggerDefinition>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<TableTriggerDefinition> triggers = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            triggers.Add(new TableTriggerDefinition
            {
                Name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                TriggerType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                EventName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Status = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                BodyPreview = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
            });
        }

        return triggers;
    }
    private static async Task<IReadOnlyList<TableOptionDefinition>> LoadTableOptionsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select 'PCTFREE', to_char(pct_free) from all_tables
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
union all
select 'PCTUSED', to_char(pct_used) from all_tables
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
union all
select 'TABLESPACE', tablespace_name from all_tables
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'",
            "Dameng" => $@"
select 'PCTFREE', to_char(pct_free) from all_tables
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
union all
select 'PCTUSED', to_char(pct_used) from all_tables
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'
union all
select 'TABLESPACE', tablespace_name from all_tables
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<TableOptionDefinition>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<TableOptionDefinition> options = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            options.Add(new TableOptionDefinition
            {
                Name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Value = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
            });
        }

        return options;
    }
    private static async Task<string> LoadTableCommentAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, schemaName));
        string escapedTable = EscapeSqlLiteral(NormalizeMetadataIdentifier(provider, tableName));
        string sql = provider.Name switch
        {
            "Oracle" => $@"
select comments
  from all_tab_comments
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'",
            "Dameng" => $@"
select comments
  from all_tab_comments
 where owner = '{escapedSchema}'
   and table_name = '{escapedTable}'",
            "SqlServer" => $@"
select cast(ep.value as nvarchar(max))
  from sys.tables t
  join sys.schemas s
    on t.schema_id = s.schema_id
  left join sys.extended_properties ep
    on ep.major_id = t.object_id
   and ep.minor_id = 0
   and ep.name = 'MS_Description'
 where s.name = '{escapedSchema}'
   and t.name = '{escapedTable}'",
            "PostgreSql" => $@"
select coalesce(obj_description(c.oid, 'pg_class'), '')
  from pg_catalog.pg_class c
  join pg_catalog.pg_namespace n
    on n.oid = c.relnamespace
 where n.nspname = '{escapedSchema}'
   and c.relname = '{escapedTable}'",
            "KingbaseES" => $@"
select coalesce(obj_description(c.oid, 'pg_class'), '')
  from pg_catalog.pg_class c
  join pg_catalog.pg_namespace n
    on n.oid = c.relnamespace
 where n.nspname = '{escapedSchema}'
   and c.relname = '{escapedTable}'",
            "MySql" or "MariaDB" => $@"
select table_comment
  from information_schema.tables
 where table_schema = '{escapedSchema}'
   and table_name = '{escapedTable}'",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar?.ToString() ?? string.Empty;
    }
    private static async Task ApplyColumnCommentAsync(DbConnection connection, TableDesignModel design, TableColumnDefinition column, CancellationToken cancellationToken)
    {
        string comment = EscapeSqlLiteral(column.Comment ?? string.Empty);
        string sql = $"comment on column {BuildQualifiedName(design.ProviderName, design.SchemaName, design.TableName)}.{QuoteIdentifier(design.ProviderName, column.Name)} is '{comment}'";
        await ExecuteNonQueryAsync(connection, sql, cancellationToken);
    }
    private static async Task SynchronizePrimaryKeyAsync(
        DbConnection connection,
        TableDesignModel updatedDesign,
        TableDesignModel originalDesign,
        CancellationToken cancellationToken)
    {
        List<string> updatedPrimaryKey = updatedDesign.Columns.Where(item => item.IsPrimaryKey).Select(item => item.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
        List<string> originalPrimaryKey = originalDesign.Columns.Where(item => item.IsPrimaryKey).Select(item => item.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();

        if (updatedPrimaryKey.SequenceEqual(originalPrimaryKey, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        string constraintName = $"PK_{updatedDesign.TableName}";
        string qualifiedName = BuildQualifiedName(updatedDesign.ProviderName, updatedDesign.SchemaName, updatedDesign.TableName);
        if (originalPrimaryKey.Count > 0)
        {
            await ExecuteNonQueryAsync(connection, $"alter table {qualifiedName} drop primary key", cancellationToken);
        }

        if (updatedPrimaryKey.Count > 0)
        {
            string columns = string.Join(", ", updatedPrimaryKey.Select(column => QuoteIdentifier(updatedDesign.ProviderName, column)));
            await ExecuteNonQueryAsync(connection, $"alter table {qualifiedName} add constraint {QuoteIdentifier(updatedDesign.ProviderName, constraintName)} primary key ({columns})", cancellationToken);
        }
    }
    private static bool HasColumnDefinitionChanged(TableColumnDefinition original, TableColumnDefinition updated)
    {
        return !string.Equals(original.DataType, updated.DataType, StringComparison.OrdinalIgnoreCase)
            || original.Length != updated.Length
            || original.Precision != updated.Precision
            || original.Scale != updated.Scale
            || original.IsNullable != updated.IsNullable;
    }
    private static string BuildCreateTableScript(TableDesignModel design)
    {
        List<string> lines = [];
        foreach (TableColumnDefinition column in design.Columns)
        {
            lines.Add($"    {BuildColumnDefinitionSql(design.ProviderName, column)}");
        }

        IReadOnlyList<string> primaryKeys = design.Columns.Where(item => item.IsPrimaryKey).Select(item => QuoteIdentifier(design.ProviderName, item.Name)).ToArray();
        if (primaryKeys.Count > 0)
        {
            lines.Add($"    constraint {QuoteIdentifier(design.ProviderName, $"PK_{design.TableName}")} primary key ({string.Join(", ", primaryKeys)})");
        }

        string createTable = $"create table {BuildQualifiedName(design.ProviderName, design.SchemaName, design.TableName)} ({Environment.NewLine}{string.Join($",{Environment.NewLine}", lines)}{Environment.NewLine});";
        IEnumerable<string> comments = design.Columns
            .Where(item => !string.IsNullOrWhiteSpace(item.Comment))
            .Select(item => $"comment on column {BuildQualifiedName(design.ProviderName, design.SchemaName, design.TableName)}.{QuoteIdentifier(design.ProviderName, item.Name)} is '{EscapeSqlLiteral(item.Comment)}';");

        return string.Join(Environment.NewLine, new[] { createTable }.Concat(comments));
    }
    private static string BuildColumnDefinitionSql(string providerName, TableColumnDefinition column)
    {
        string dataType = NormalizeDataType(column.DataType);
        string size = dataType.ToUpperInvariant() switch
        {
            "VARCHAR2" or "VARCHAR" or "CHAR" or "NCHAR" or "NVARCHAR2" when column.Length > 0 => $"({column.Length})",
            "NUMBER" or "DECIMAL" when column.Precision > 0 && column.Scale > 0 => $"({column.Precision}, {column.Scale})",
            "NUMBER" or "DECIMAL" when column.Precision > 0 => $"({column.Precision})",
            _ => string.Empty
        };
        string nullable = column.IsNullable ? string.Empty : " not null";
        return $"{QuoteIdentifier(providerName, column.Name)} {dataType}{size}{nullable}";
    }
    private static string BuildQualifiedName(DatabaseProviderDefinition provider, string schemaName, string tableName)
    {
        return BuildQualifiedName(provider.Name, schemaName, tableName);
    }
    private static string BuildQualifiedName(string providerName, string schemaName, string tableName)
    {
        string quotedTable = QuoteIdentifier(providerName, tableName);
        return string.IsNullOrWhiteSpace(schemaName)
            ? quotedTable
            : $"{QuoteIdentifier(providerName, schemaName)}.{quotedTable}";
    }
    private static string QuoteIdentifier(string providerName, string identifier)
    {
        string normalized = identifier?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("表设计缺少必要的表名或字段名。");
        }

        return providerName switch
        {
            "SqlServer" => $"[{normalized.Replace("]", "]]")}]",
            "MySql" or "MariaDB" => $"`{normalized.Replace("`", "``")}`",
            _ => $"\"{normalized.Replace("\"", "\"\"")}\""
        };
    }

    private static string NormalizeDataType(string dataType)
    {
        string normalized = dataType?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Any(static ch => !(char.IsLetterOrDigit(ch) || ch == '_' || char.IsWhiteSpace(ch))))
        {
            throw new InvalidOperationException("字段类型包含不支持的字符，已拒绝生成表结构 SQL。");
        }

        return normalized;
    }
    private static string FormatSqlLiteral(DatabaseProviderDefinition provider, object value)
    {
        if (value is null || value == DBNull.Value)
        {
            return "null";
        }

        return value switch
        {
            string text => $"'{EscapeSqlLiteral(text)}'",
            DateTime dateTime when string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase)
                => $"to_timestamp('{dateTime:yyyy-MM-dd HH:mm:ss.fff}', 'yyyy-mm-dd hh24:mi:ss.ff3')",
            DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.fff}'",
            bool boolean => boolean ? "1" : "0",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
        };
    }
    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }
}
