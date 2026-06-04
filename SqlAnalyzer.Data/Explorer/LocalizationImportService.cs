using System.Data.Common;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;

public sealed class LocalizationImportService : ILocalizationImportService
{
    private readonly DbProviderRuntime _runtime;
    public LocalizationImportService(IDatabaseProviderCatalog providerCatalog)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
    }
    public async Task<LocalizationDictionarySnapshot> ImportFromCommentsAsync(
        ConnectionProfile profile,
        IReadOnlyList<string>? schemas = null,
        CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);

        string[] targetSchemas = ResolveSchemas(profile, schemas);
        LocalizedObjectLabel[] objects = await LoadObjectLabelsAsync(connection, provider, profile, targetSchemas, cancellationToken);
        LocalizedColumnLabel[] columns = await LoadColumnLabelsAsync(connection, provider, profile, targetSchemas, cancellationToken);

        return new LocalizationDictionarySnapshot
        {
            LocaleCode = "zh-CN",
            ObjectLabels = objects,
            ColumnLabels = columns
        };
    }
    private async Task<LocalizedObjectLabel[]> LoadObjectLabelsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        ConnectionProfile profile,
        IReadOnlyList<string> schemas,
        CancellationToken cancellationToken)
    {
        string sql = BuildObjectCommentsSql(provider, schemas);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<LocalizedObjectLabel> items = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            string schemaName = reader["SCHEMA_NAME"]?.ToString()?.Trim() ?? string.Empty;
            string objectName = reader["OBJECT_NAME"]?.ToString()?.Trim() ?? string.Empty;
            string objectType = reader["OBJECT_TYPE"]?.ToString()?.Trim() ?? string.Empty;
            string comment = reader["COMMENTS"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(schemaName) ||
                string.IsNullOrWhiteSpace(objectName) ||
                string.IsNullOrWhiteSpace(objectType) ||
                string.IsNullOrWhiteSpace(comment))
            {
                continue;
            }

            items.Add(new LocalizedObjectLabel
            {
                ProviderName = profile.ProviderName,
                DatabaseName = profile.Database,
                SchemaName = schemaName,
                ObjectName = objectName,
                ObjectType = NormalizeObjectType(objectType),
                DisplayName = comment,
                Description = comment,
                Source = "comment",
                LocaleCode = "zh-CN",
                LastUpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        return items
            .GroupBy(item => $"{item.SchemaName}.{item.ObjectName}.{item.ObjectType}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }
    private async Task<LocalizedColumnLabel[]> LoadColumnLabelsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        ConnectionProfile profile,
        IReadOnlyList<string> schemas,
        CancellationToken cancellationToken)
    {
        string sql = BuildColumnCommentsSql(provider, schemas);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<LocalizedColumnLabel> items = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            string schemaName = reader["SCHEMA_NAME"]?.ToString()?.Trim() ?? string.Empty;
            string objectName = reader["OBJECT_NAME"]?.ToString()?.Trim() ?? string.Empty;
            string columnName = reader["COLUMN_NAME"]?.ToString()?.Trim() ?? string.Empty;
            string comment = reader["COMMENTS"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(schemaName) ||
                string.IsNullOrWhiteSpace(objectName) ||
                string.IsNullOrWhiteSpace(columnName) ||
                string.IsNullOrWhiteSpace(comment))
            {
                continue;
            }

            items.Add(new LocalizedColumnLabel
            {
                ProviderName = profile.ProviderName,
                DatabaseName = profile.Database,
                SchemaName = schemaName,
                ObjectName = objectName,
                ColumnName = columnName,
                DisplayName = comment,
                Description = comment,
                Source = "comment",
                LocaleCode = "zh-CN",
                LastUpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        return items
            .GroupBy(item => $"{item.SchemaName}.{item.ObjectName}.{item.ColumnName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
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
    private static string[] ResolveSchemas(ConnectionProfile profile, IReadOnlyList<string>? schemas)
    {
        if (schemas != null && schemas.Count > 0)
        {
            return schemas.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(profile.Schema))
        {
            return [profile.Schema.Trim()];
        }

        if (!string.IsNullOrWhiteSpace(profile.UserName))
        {
            return [profile.UserName.Trim()];
        }

        return [];
    }
    private static string BuildObjectCommentsSql(DatabaseProviderDefinition provider, IReadOnlyList<string> schemas)
    {
        if (schemas.Count == 0)
        {
            return string.Empty;
        }

        string inListUpper = BuildSqlInList(schemas.Select(item => item.ToUpperInvariant()));
        string inList = BuildSqlInList(schemas);

        return provider.Name switch
        {
            "Oracle" or "Dameng" => $@"
select owner as SCHEMA_NAME, table_name as OBJECT_NAME, table_type as OBJECT_TYPE, comments as COMMENTS
from all_tab_comments
where owner in ({inListUpper}) and comments is not null",
            "MySql" or "MariaDB" => $@"
select table_schema as SCHEMA_NAME, table_name as OBJECT_NAME, table_type as OBJECT_TYPE, table_comment as COMMENTS
from information_schema.tables
where table_schema in ({inList}) and table_comment <> ''",
            "PostgreSql" or "KingbaseES" => $@"
select n.nspname as SCHEMA_NAME,
       c.relname as OBJECT_NAME,
       case c.relkind when 'r' then 'table' when 'v' then 'view' when 'm' then 'materializedview' else c.relkind::text end as OBJECT_TYPE,
       pgd.description as COMMENTS
from pg_catalog.pg_class c
join pg_catalog.pg_namespace n on n.oid = c.relnamespace
join pg_catalog.pg_description pgd on pgd.objoid = c.oid and pgd.objsubid = 0
where n.nspname in ({inList}) and c.relkind in ('r','v','m') and pgd.description is not null and pgd.description <> ''",
            "SqlServer" => $@"
select s.name as SCHEMA_NAME,
       o.name as OBJECT_NAME,
       case o.type when 'U' then 'table' when 'V' then 'view' else o.type end as OBJECT_TYPE,
       cast(ep.value as nvarchar(4000)) as COMMENTS
from sys.objects o
join sys.schemas s on s.schema_id = o.schema_id
join sys.extended_properties ep on ep.major_id = o.object_id and ep.minor_id = 0 and ep.name = 'MS_Description'
where s.name in ({inList}) and o.type in ('U','V')",
            _ => string.Empty
        };
    }
    private static string BuildColumnCommentsSql(DatabaseProviderDefinition provider, IReadOnlyList<string> schemas)
    {
        if (schemas.Count == 0)
        {
            return string.Empty;
        }

        string inListUpper = BuildSqlInList(schemas.Select(item => item.ToUpperInvariant()));
        string inList = BuildSqlInList(schemas);

        return provider.Name switch
        {
            "Oracle" or "Dameng" => $@"
select owner as SCHEMA_NAME, table_name as OBJECT_NAME, column_name as COLUMN_NAME, comments as COMMENTS
from all_col_comments
where owner in ({inListUpper}) and comments is not null",
            "MySql" or "MariaDB" => $@"
select table_schema as SCHEMA_NAME, table_name as OBJECT_NAME, column_name as COLUMN_NAME, column_comment as COMMENTS
from information_schema.columns
where table_schema in ({inList}) and column_comment <> ''",
            "PostgreSql" or "KingbaseES" => $@"
select n.nspname as SCHEMA_NAME,
       c.relname as OBJECT_NAME,
       a.attname as COLUMN_NAME,
       pgd.description as COMMENTS
from pg_catalog.pg_class c
join pg_catalog.pg_namespace n on n.oid = c.relnamespace
join pg_catalog.pg_attribute a on a.attrelid = c.oid and a.attnum > 0 and not a.attisdropped
join pg_catalog.pg_description pgd on pgd.objoid = c.oid and pgd.objsubid = a.attnum
where n.nspname in ({inList}) and c.relkind in ('r','v','m') and pgd.description is not null and pgd.description <> ''",
            "SqlServer" => $@"
select s.name as SCHEMA_NAME,
       o.name as OBJECT_NAME,
       c.name as COLUMN_NAME,
       cast(ep.value as nvarchar(4000)) as COMMENTS
from sys.columns c
join sys.objects o on o.object_id = c.object_id
join sys.schemas s on s.schema_id = o.schema_id
join sys.extended_properties ep on ep.major_id = c.object_id and ep.minor_id = c.column_id and ep.name = 'MS_Description'
where s.name in ({inList}) and o.type in ('U','V')",
            _ => string.Empty
        };
    }
    private static string NormalizeObjectType(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "table" or "base table" => "table",
            "view" => "view",
            "materialized view" or "materializedview" => "materializedview",
            _ => value.Trim().ToLowerInvariant()
        };
    }
    private static string BuildSqlInList(IEnumerable<string> values)
    {
        return string.Join(", ", values.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => $"'{EscapeSqlLiteral(item.Trim())}'"));
    }
    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }
}
