using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;
public sealed class ModelDiagramService : IModelDiagramService
{
    private sealed record TableColumnInfo(string TableName, string ColumnName, string DataType);

    private sealed class RelationAccumulator
    {
        public string ConstraintName { get; init; } = string.Empty;

        public string ParentTable { get; init; } = string.Empty;

        public string ChildTable { get; init; } = string.Empty;

        public List<(int Ordinal, string ParentColumn, string ChildColumn)> ColumnPairs { get; } = [];
    }

    private readonly DbProviderRuntime _runtime;
    public ModelDiagramService(IDatabaseProviderCatalog providerCatalog)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
    }
    public async Task<ModelDiagramWorkspace> LoadSchemaModelAsync(
        ConnectionProfile profile,
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        string effectiveSchema = ResolveEffectiveSchema(profile, schemaName);

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        IReadOnlyList<string> tables = await LoadTablesAsync(connection, provider.Name, effectiveSchema, cancellationToken);
        IReadOnlyDictionary<string, string> tableComments = await LoadTableCommentsAsync(connection, provider.Name, effectiveSchema, cancellationToken);
        IReadOnlyList<TableColumnInfo> columns = await LoadColumnsAsync(connection, provider.Name, effectiveSchema, cancellationToken);
        IReadOnlyDictionary<string, string> columnComments = await LoadColumnCommentsAsync(connection, provider.Name, effectiveSchema, cancellationToken);
        IReadOnlyDictionary<string, IReadOnlyList<string>> primaryKeys = await LoadPrimaryKeysAsync(connection, provider.Name, effectiveSchema, cancellationToken);
        IReadOnlyList<ModelRelationEdge> relations = await LoadForeignKeysAsync(connection, provider.Name, effectiveSchema, cancellationToken);

        return BuildWorkspace(profile, provider.Name, effectiveSchema, tables, tableComments, columns, columnComments, primaryKeys, relations);
    }
    public async Task<ModelDiagramWorkspace> LoadTableNeighborhoodAsync(
        ConnectionProfile profile,
        string schemaName,
        string tableName,
        int depth,
        CancellationToken cancellationToken = default)
    {
        return BuildNeighborhood(await LoadSchemaModelAsync(profile, schemaName, cancellationToken), tableName, depth);
    }
    public Task<IReadOnlyList<ModelRelationExportRow>> ExportRelationRowsAsync(
        ConnectionProfile profile,
        ModelDiagramWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, ModelTableNode> tableLookup = workspace.Tables.ToDictionary(item => item.TableName, StringComparer.OrdinalIgnoreCase);
        List<ModelRelationExportRow> rows = [];

        foreach (ModelRelationEdge relation in workspace.Relations
                     .OrderBy(item => item.ChildTable, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.ConstraintName, StringComparer.OrdinalIgnoreCase))
        {
            tableLookup.TryGetValue(relation.ParentTable, out ModelTableNode? parentTable);
            tableLookup.TryGetValue(relation.ChildTable, out ModelTableNode? childTable);
            rows.Add(new ModelRelationExportRow
            {
                ParentTable = relation.ParentTable,
                ParentComment = parentTable?.CommentText ?? string.Empty,
                ChildTable = relation.ChildTable,
                ChildComment = childTable?.CommentText ?? string.Empty,
                ParentColumnsText = string.Join(", ", relation.ParentColumns),
                ChildColumnsText = string.Join(", ", relation.ChildColumns),
                ConstraintName = relation.ConstraintName
            });
        }

        return Task.FromResult<IReadOnlyList<ModelRelationExportRow>>(rows);
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
    private static string ResolveEffectiveSchema(ConnectionProfile profile, string schemaName)
    {
        string effectiveSchema = string.IsNullOrWhiteSpace(schemaName) ? (profile.Schema ?? string.Empty) : schemaName;
        if (string.IsNullOrWhiteSpace(effectiveSchema))
        {
            throw new InvalidOperationException("当前数据模型工作台未绑定有效模式。");
        }

        return effectiveSchema.Trim();
    }
    private async Task<IReadOnlyList<string>> LoadTablesAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        List<string> tables = [];

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = BuildTablesSql(providerName, schemaName);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = ReadString(reader, "TABLE_NAME");
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                tables.Add(tableName);
            }
        }

        return tables.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private async Task<IReadOnlyDictionary<string, string>> LoadTableCommentsAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = BuildTableCommentsSql(providerName, schemaName);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = ReadString(reader, "TABLE_NAME");
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                result[tableName] = ReadString(reader, "COMMENTS");
            }
        }

        return result;
    }
    private async Task<IReadOnlyList<TableColumnInfo>> LoadColumnsAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        List<TableColumnInfo> result = [];

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = BuildColumnsSql(providerName, schemaName);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = ReadString(reader, "TABLE_NAME");
            string columnName = ReadString(reader, "COLUMN_NAME");
            if (!string.IsNullOrWhiteSpace(tableName) && !string.IsNullOrWhiteSpace(columnName))
            {
                result.Add(new TableColumnInfo(tableName, columnName, ReadString(reader, "DATA_TYPE")));
            }
        }

        return result;
    }
    private async Task<IReadOnlyDictionary<string, string>> LoadColumnCommentsAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = BuildColumnCommentsSql(providerName, schemaName);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = ReadString(reader, "TABLE_NAME");
            string columnName = ReadString(reader, "COLUMN_NAME");
            if (!string.IsNullOrWhiteSpace(tableName) && !string.IsNullOrWhiteSpace(columnName))
            {
                result[BuildColumnKey(tableName, columnName)] = ReadString(reader, "COMMENTS");
            }
        }

        return result;
    }
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadPrimaryKeysAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        Dictionary<string, List<(int Ordinal, string ColumnName)>> groups = new(StringComparer.OrdinalIgnoreCase);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = BuildPrimaryKeysSql(providerName, schemaName);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = ReadString(reader, "TABLE_NAME");
            string columnName = ReadString(reader, "COLUMN_NAME");
            if (!string.IsNullOrWhiteSpace(tableName) && !string.IsNullOrWhiteSpace(columnName))
            {
                if (!groups.TryGetValue(tableName, out List<(int Ordinal, string ColumnName)>? items))
                {
                    items = [];
                    groups[tableName] = items;
                }

                items.Add((ReadInt(reader, "ORDINAL"), columnName));
            }
        }

        return groups.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)item.Value.OrderBy(entry => entry.Ordinal).Select(entry => entry.ColumnName).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
    private async Task<IReadOnlyList<ModelRelationEdge>> LoadForeignKeysAsync(DbConnection connection, string providerName, string schemaName, CancellationToken cancellationToken)
    {
        Dictionary<string, RelationAccumulator> relations = new(StringComparer.OrdinalIgnoreCase);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = BuildForeignKeysSql(providerName, schemaName);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string constraintName = ReadString(reader, "CONSTRAINT_NAME");
            string parentTable = ReadString(reader, "PARENT_TABLE");
            string parentColumn = ReadString(reader, "PARENT_COLUMN");
            string childTable = ReadString(reader, "CHILD_TABLE");
            string childColumn = ReadString(reader, "CHILD_COLUMN");
            if (string.IsNullOrWhiteSpace(parentTable) || string.IsNullOrWhiteSpace(childTable) ||
                string.IsNullOrWhiteSpace(parentColumn) || string.IsNullOrWhiteSpace(childColumn))
            {
                continue;
            }

            string relationKey = $"{childTable}|{constraintName}|{parentTable}";
            if (!relations.TryGetValue(relationKey, out RelationAccumulator? accumulator))
            {
                accumulator = new RelationAccumulator
                {
                    ConstraintName = constraintName,
                    ParentTable = parentTable,
                    ChildTable = childTable
                };
                relations[relationKey] = accumulator;
            }

            accumulator.ColumnPairs.Add((ReadInt(reader, "ORDINAL"), parentColumn, childColumn));
        }

        return relations.Values
            .Select(item => new ModelRelationEdge
            {
                ConstraintName = item.ConstraintName,
                ParentTable = item.ParentTable,
                ChildTable = item.ChildTable,
                ParentColumns = item.ColumnPairs.OrderBy(entry => entry.Ordinal).Select(entry => entry.ParentColumn).ToArray(),
                ChildColumns = item.ColumnPairs.OrderBy(entry => entry.Ordinal).Select(entry => entry.ChildColumn).ToArray()
            })
            .OrderBy(item => item.ChildTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ConstraintName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private static ModelDiagramWorkspace BuildWorkspace(
        ConnectionProfile profile,
        string providerName,
        string schemaName,
        IReadOnlyList<string> tables,
        IReadOnlyDictionary<string, string> tableComments,
        IReadOnlyList<TableColumnInfo> columns,
        IReadOnlyDictionary<string, string> columnComments,
        IReadOnlyDictionary<string, IReadOnlyList<string>> primaryKeys,
        IReadOnlyList<ModelRelationEdge> relations)
    {
        HashSet<string> knownTables = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ModelRelationEdge> scopedRelations = relations
            .Where(item => knownTables.Contains(item.ParentTable) && knownTables.Contains(item.ChildTable))
            .ToArray();

        Dictionary<string, List<TableColumnInfo>> columnLookup = columns
            .Where(item => knownTables.Contains(item.TableName))
            .GroupBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.ToList(), StringComparer.OrdinalIgnoreCase);

        List<ModelTableNode> nodes = [];
        foreach (string tableName in tables)
        {
            IReadOnlyList<string> pkColumns = primaryKeys.TryGetValue(tableName, out IReadOnlyList<string>? pk) ? pk : Array.Empty<string>();
            HashSet<string> primaryKeySet = pkColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> foreignKeySet = scopedRelations
                .Where(item => string.Equals(item.ChildTable, tableName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.ChildColumns)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<ModelColumnNode> tableColumns = [];
            if (columnLookup.TryGetValue(tableName, out List<TableColumnInfo>? tableColumnInfos))
            {
                foreach (TableColumnInfo column in tableColumnInfos)
                {
                    tableColumns.Add(new ModelColumnNode
                    {
                        ColumnName = column.ColumnName,
                        DisplayName = column.ColumnName,
                        CommentText = columnComments.TryGetValue(BuildColumnKey(column.TableName, column.ColumnName), out string? comment) ? comment : string.Empty,
                        DataType = column.DataType,
                        IsPrimaryKey = primaryKeySet.Contains(column.ColumnName),
                        IsForeignKey = foreignKeySet.Contains(column.ColumnName)
                    });
                }
            }

            nodes.Add(new ModelTableNode
            {
                SchemaName = schemaName,
                TableName = tableName,
                DisplayName = tableName,
                CommentText = tableComments.TryGetValue(tableName, out string? tableComment) ? tableComment : string.Empty,
                Columns = tableColumns,
                PrimaryKeyColumns = pkColumns,
                ForeignKeyCount = scopedRelations.Count(item => string.Equals(item.ChildTable, tableName, StringComparison.OrdinalIgnoreCase)),
                ReferencedByCount = scopedRelations.Count(item => string.Equals(item.ParentTable, tableName, StringComparison.OrdinalIgnoreCase)),
                ReferencesCount = scopedRelations.Count(item => string.Equals(item.ChildTable, tableName, StringComparison.OrdinalIgnoreCase))
            });
        }

        return new ModelDiagramWorkspace
        {
            ProviderName = providerName,
            ConnectionProfileId = profile.Id,
            ConnectionName = profile.Name,
            SchemaName = schemaName,
            LoadedAt = DateTimeOffset.UtcNow,
            Tables = nodes,
            Relations = scopedRelations
        };
    }
    private static ModelDiagramWorkspace BuildNeighborhood(ModelDiagramWorkspace workspace, string tableName, int depth)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return workspace;
        }

        HashSet<string> visibleNames = [tableName];
        HashSet<string> frontier = [tableName];
        int levelCount = Math.Max(1, depth);

        for (int level = 0; level < levelCount; level++)
        {
            HashSet<string> next = [];
            foreach (ModelRelationEdge relation in workspace.Relations)
            {
                if (frontier.Contains(relation.ParentTable))
                {
                    next.Add(relation.ChildTable);
                }

                if (frontier.Contains(relation.ChildTable))
                {
                    next.Add(relation.ParentTable);
                }
            }

            next.ExceptWith(visibleNames);
            if (next.Count == 0)
            {
                break;
            }

            visibleNames.UnionWith(next);
            frontier = next;
        }

        return new ModelDiagramWorkspace
        {
            ProviderName = workspace.ProviderName,
            ConnectionProfileId = workspace.ConnectionProfileId,
            ConnectionName = workspace.ConnectionName,
            SchemaName = workspace.SchemaName,
            LoadedAt = workspace.LoadedAt,
            Tables = workspace.Tables.Where(item => visibleNames.Contains(item.TableName)).ToArray(),
            Relations = workspace.Relations.Where(item => visibleNames.Contains(item.ParentTable) && visibleNames.Contains(item.ChildTable)).ToArray()
        };
    }
    private static string BuildTablesSql(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" or "Dameng" => $"select table_name as TABLE_NAME from all_tables where owner = '{schema.ToUpperInvariant()}' order by table_name",
            "PostgreSql" or "KingbaseES" => $"select table_name as TABLE_NAME from information_schema.tables where table_schema = '{schema}' and table_type = 'BASE TABLE' order by table_name",
            "MySql" or "MariaDB" => $"select table_name as TABLE_NAME from information_schema.tables where table_schema = '{schema}' and table_type = 'BASE TABLE' order by table_name",
            "SqlServer" => $"select t.name as TABLE_NAME from sys.tables t join sys.schemas s on s.schema_id = t.schema_id where s.name = '{schema}' order by t.name",
            "SQLite" => $"select name as TABLE_NAME from pragma_table_list where schema = '{schema}' and type in ('table', 'virtual') and name not like 'sqlite_%' order by name",
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported by model diagram.")
        };
    }

    private static string BuildTableCommentsSql(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" or "Dameng" => $"select table_name as TABLE_NAME, comments as COMMENTS from all_tab_comments where owner = '{schema.ToUpperInvariant()}' and table_type = 'TABLE'",
            "PostgreSql" or "KingbaseES" => $"select c.relname as TABLE_NAME, coalesce(obj_description(c.oid), '') as COMMENTS from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = '{schema}' and c.relkind = 'r'",
            "MySql" or "MariaDB" => $"select table_name as TABLE_NAME, coalesce(table_comment, '') as COMMENTS from information_schema.tables where table_schema = '{schema}' and table_type = 'BASE TABLE'",
            "SqlServer" => $"select t.name as TABLE_NAME, coalesce(cast(ep.value as nvarchar(max)), '') as COMMENTS from sys.tables t join sys.schemas s on s.schema_id = t.schema_id left join sys.extended_properties ep on ep.major_id = t.object_id and ep.minor_id = 0 and ep.name = 'MS_Description' where s.name = '{schema}'",
            "SQLite" => $"select name as TABLE_NAME, '' as COMMENTS from pragma_table_list where schema = '{schema}' and type in ('table', 'virtual') and name not like 'sqlite_%'",
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported by model diagram.")
        };
    }

    private static string BuildColumnsSql(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" or "Dameng" => $"select table_name as TABLE_NAME, column_name as COLUMN_NAME, data_type as DATA_TYPE from all_tab_columns where owner = '{schema.ToUpperInvariant()}' order by table_name, column_id",
            "PostgreSql" or "KingbaseES" => $"select table_name as TABLE_NAME, column_name as COLUMN_NAME, data_type as DATA_TYPE from information_schema.columns where table_schema = '{schema}' order by table_name, ordinal_position",
            "MySql" or "MariaDB" => $"select table_name as TABLE_NAME, column_name as COLUMN_NAME, column_type as DATA_TYPE from information_schema.columns where table_schema = '{schema}' order by table_name, ordinal_position",
            "SqlServer" => $"select t.name as TABLE_NAME, c.name as COLUMN_NAME, ty.name as DATA_TYPE from sys.columns c join sys.tables t on t.object_id = c.object_id join sys.schemas s on s.schema_id = t.schema_id join sys.types ty on ty.user_type_id = c.user_type_id where s.name = '{schema}' order by t.name, c.column_id",
            "SQLite" => $"select t.name as TABLE_NAME, p.name as COLUMN_NAME, p.type as DATA_TYPE from pragma_table_list t join pragma_table_info(t.name) p where t.schema = '{schema}' and t.type in ('table', 'virtual') and t.name not like 'sqlite_%' order by t.name, p.cid",
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported by model diagram.")
        };
    }

    private static string BuildColumnCommentsSql(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" or "Dameng" => $"select table_name as TABLE_NAME, column_name as COLUMN_NAME, comments as COMMENTS from all_col_comments where owner = '{schema.ToUpperInvariant()}'",
            "PostgreSql" or "KingbaseES" => $"select c.table_name as TABLE_NAME, c.column_name as COLUMN_NAME, coalesce(pgd.description, '') as COMMENTS from information_schema.columns c join pg_class pc on pc.relname = c.table_name join pg_namespace pn on pn.oid = pc.relnamespace and pn.nspname = c.table_schema join pg_attribute pa on pa.attrelid = pc.oid and pa.attname = c.column_name left join pg_description pgd on pgd.objoid = pc.oid and pgd.objsubid = pa.attnum where c.table_schema = '{schema}'",
            "MySql" or "MariaDB" => $"select table_name as TABLE_NAME, column_name as COLUMN_NAME, coalesce(column_comment, '') as COMMENTS from information_schema.columns where table_schema = '{schema}'",
            "SqlServer" => $"select t.name as TABLE_NAME, c.name as COLUMN_NAME, coalesce(cast(ep.value as nvarchar(max)), '') as COMMENTS from sys.columns c join sys.tables t on t.object_id = c.object_id join sys.schemas s on s.schema_id = t.schema_id left join sys.extended_properties ep on ep.major_id = c.object_id and ep.minor_id = c.column_id and ep.name = 'MS_Description' where s.name = '{schema}'",
            "SQLite" => $"select t.name as TABLE_NAME, p.name as COLUMN_NAME, '' as COMMENTS from pragma_table_list t join pragma_table_info(t.name) p where t.schema = '{schema}' and t.type in ('table', 'virtual') and t.name not like 'sqlite_%'",
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported by model diagram.")
        };
    }

    private static string BuildPrimaryKeysSql(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" or "Dameng" => $"select acc.table_name as TABLE_NAME, acc.column_name as COLUMN_NAME, acc.position as ORDINAL from all_constraints ac join all_cons_columns acc on ac.owner = acc.owner and ac.constraint_name = acc.constraint_name where ac.owner = '{schema.ToUpperInvariant()}' and ac.constraint_type = 'P' order by acc.table_name, acc.position",
            "PostgreSql" or "KingbaseES" => $"select kcu.table_name as TABLE_NAME, kcu.column_name as COLUMN_NAME, kcu.ordinal_position as ORDINAL from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name = kcu.constraint_name and tc.table_schema = kcu.table_schema and tc.table_name = kcu.table_name where tc.table_schema = '{schema}' and tc.constraint_type = 'PRIMARY KEY' order by kcu.table_name, kcu.ordinal_position",
            "MySql" or "MariaDB" => $"select kcu.table_name as TABLE_NAME, kcu.column_name as COLUMN_NAME, kcu.ordinal_position as ORDINAL from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name = kcu.constraint_name and tc.table_schema = kcu.table_schema and tc.table_name = kcu.table_name where tc.table_schema = '{schema}' and tc.constraint_type = 'PRIMARY KEY' order by kcu.table_name, kcu.ordinal_position",
            "SqlServer" => $"select t.name as TABLE_NAME, c.name as COLUMN_NAME, ic.key_ordinal as ORDINAL from sys.key_constraints kc join sys.tables t on t.object_id = kc.parent_object_id join sys.schemas s on s.schema_id = t.schema_id join sys.index_columns ic on ic.object_id = kc.parent_object_id and ic.index_id = kc.unique_index_id join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id where s.name = '{schema}' and kc.type = 'PK' order by t.name, ic.key_ordinal",
            "SQLite" => $"select t.name as TABLE_NAME, p.name as COLUMN_NAME, p.pk as ORDINAL from pragma_table_list t join pragma_table_info(t.name) p where t.schema = '{schema}' and t.type in ('table', 'virtual') and t.name not like 'sqlite_%' and p.pk > 0 order by t.name, p.pk",
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported by model diagram.")
        };
    }

    private static string BuildForeignKeysSql(string providerName, string schemaName)
    {
        string schema = EscapeSqlLiteral(schemaName);
        return providerName switch
        {
            "Oracle" or "Dameng" => $"select fk.constraint_name as CONSTRAINT_NAME, pk_cols.table_name as PARENT_TABLE, pk_cols.column_name as PARENT_COLUMN, fk_cols.table_name as CHILD_TABLE, fk_cols.column_name as CHILD_COLUMN, fk_cols.position as ORDINAL from all_constraints fk join all_cons_columns fk_cols on fk.owner = fk_cols.owner and fk.constraint_name = fk_cols.constraint_name join all_constraints pk on fk.r_owner = pk.owner and fk.r_constraint_name = pk.constraint_name join all_cons_columns pk_cols on pk.owner = pk_cols.owner and pk.constraint_name = pk_cols.constraint_name and fk_cols.position = pk_cols.position where fk.owner = '{schema.ToUpperInvariant()}' and fk.constraint_type = 'R' order by fk_cols.table_name, fk.constraint_name, fk_cols.position",
            "PostgreSql" or "KingbaseES" => $"select con.conname as CONSTRAINT_NAME, parent.relname as PARENT_TABLE, parent_col.attname as PARENT_COLUMN, child.relname as CHILD_TABLE, child_col.attname as CHILD_COLUMN, ord.n as ORDINAL from pg_constraint con join pg_class child on child.oid = con.conrelid join pg_namespace child_ns on child_ns.oid = child.relnamespace join pg_class parent on parent.oid = con.confrelid join generate_subscripts(con.conkey, 1) ord(n) on true join pg_attribute child_col on child_col.attrelid = child.oid and child_col.attnum = con.conkey[ord.n] join pg_attribute parent_col on parent_col.attrelid = parent.oid and parent_col.attnum = con.confkey[ord.n] where con.contype = 'f' and child_ns.nspname = '{schema}' order by child.relname, con.conname, ord.n",
            "MySql" or "MariaDB" => $"select kcu.constraint_name as CONSTRAINT_NAME, kcu.referenced_table_name as PARENT_TABLE, kcu.referenced_column_name as PARENT_COLUMN, kcu.table_name as CHILD_TABLE, kcu.column_name as CHILD_COLUMN, kcu.ordinal_position as ORDINAL from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name = kcu.constraint_name and tc.table_schema = kcu.table_schema and tc.table_name = kcu.table_name where tc.table_schema = '{schema}' and tc.constraint_type = 'FOREIGN KEY' and kcu.referenced_table_name is not null order by kcu.table_name, kcu.constraint_name, kcu.ordinal_position",
            "SqlServer" => $"select fk.name as CONSTRAINT_NAME, pt.name as PARENT_TABLE, pc.name as PARENT_COLUMN, ct.name as CHILD_TABLE, cc.name as CHILD_COLUMN, fkc.constraint_column_id as ORDINAL from sys.foreign_keys fk join sys.foreign_key_columns fkc on fkc.constraint_object_id = fk.object_id join sys.tables pt on pt.object_id = fk.referenced_object_id join sys.columns pc on pc.object_id = pt.object_id and pc.column_id = fkc.referenced_column_id join sys.tables ct on ct.object_id = fk.parent_object_id join sys.columns cc on cc.object_id = ct.object_id and cc.column_id = fkc.parent_column_id join sys.schemas s on s.schema_id = ct.schema_id where s.name = '{schema}' order by ct.name, fk.name, fkc.constraint_column_id",
            "SQLite" => $"select t.name || '_' || fk.id as CONSTRAINT_NAME, fk.\"table\" as PARENT_TABLE, fk.\"to\" as PARENT_COLUMN, t.name as CHILD_TABLE, fk.\"from\" as CHILD_COLUMN, fk.seq + 1 as ORDINAL from pragma_table_list t join pragma_foreign_key_list(t.name) fk where t.schema = '{schema}' and t.type in ('table', 'virtual') and t.name not like 'sqlite_%' order by t.name, fk.id, fk.seq",
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported by model diagram.")
        };
    }
    private static string BuildColumnKey(string tableName, string columnName)
    {
        return $"{tableName}\u001f{columnName}";
    }
    private static string ReadString(DbDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? string.Empty;
    }
    private static int ReadInt(DbDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        object value = reader.GetValue(ordinal);
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            short shortValue => shortValue,
            decimal decimalValue => (int)decimalValue,
            _ => Convert.ToInt32(value)
        };
    }
    private static string EscapeSqlLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }
}
