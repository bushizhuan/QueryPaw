using System.Collections.Concurrent;
using System.Data.Common;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;

public sealed class CompletionMetadataService : ICompletionMetadataService
{
    private readonly DbProviderRuntime _runtime;
    private readonly ILocalizationResolver _localizationResolver;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _schemaCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _tableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _viewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _materializedViewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _synonymCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _columnCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> _schemaColumnCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _objectCommentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _columnCommentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<CompletionEntry>> _completionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<CompletionEntry>> _relationCompletionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<CompletionEntry>> _objectColumnCompletionCache = new(StringComparer.OrdinalIgnoreCase);
    public CompletionMetadataService(IDatabaseProviderCatalog providerCatalog, ILocalizationResolver localizationResolver)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
        _localizationResolver = localizationResolver;
    }
    public IReadOnlyList<CompletionEntry> GetCachedCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null)
    {
        string normalizedPreferredSchema = preferredSchema?.Trim() ?? string.Empty;
        string cacheKey = $"{profile.Id}:{profile.ProviderName}:{profile.Server}:{profile.Database}:{profile.Schema}:{normalizedPreferredSchema}";
        return _completionCache.TryGetValue(cacheKey, out IReadOnlyList<CompletionEntry>? cached)
            ? cached
            : Array.Empty<CompletionEntry>();
    }
    public IReadOnlyList<CompletionEntry> GetCachedRelationCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null)
    {
        string normalizedPreferredSchema = preferredSchema?.Trim() ?? string.Empty;
        string cacheKey = $"{profile.Id}:{profile.ProviderName}:{profile.Server}:{profile.Database}:{profile.Schema}:{normalizedPreferredSchema}";
        return _relationCompletionCache.TryGetValue(cacheKey, out IReadOnlyList<CompletionEntry>? cached)
            ? cached
            : Array.Empty<CompletionEntry>();
    }
    public IReadOnlyList<CompletionEntry> GetCachedObjectColumnCompletionSnapshot(ConnectionProfile profile, string objectName, string? preferredSchema = null)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return Array.Empty<CompletionEntry>();
        }

        string normalizedPreferredSchema = preferredSchema?.Trim() ?? string.Empty;
        string normalizedObjectName = objectName.Trim();
        string cacheKey = $"{profile.Id}:{profile.ProviderName}:{profile.Server}:{profile.Database}:{profile.Schema}:{normalizedPreferredSchema}:columns:{normalizedObjectName}";
        return _objectColumnCompletionCache.TryGetValue(cacheKey, out IReadOnlyList<CompletionEntry>? cached)
            ? cached
            : Array.Empty<CompletionEntry>();
    }
    public async Task<IReadOnlyList<CompletionEntry>> LoadCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default)
    {
        string normalizedPreferredSchema = preferredSchema?.Trim() ?? string.Empty;
        string cacheKey = $"{profile.Id}:{profile.ProviderName}:{profile.Server}:{profile.Database}:{profile.Schema}:{normalizedPreferredSchema}";
        if (_completionCache.TryGetValue(cacheKey, out IReadOnlyList<CompletionEntry>? cached))
        {
            return cached;
        }

        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<CompletionEntry>();
        }

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        IReadOnlyList<string> schemas = await GetSchemasAsync(connection, provider, profile, cancellationToken);
        string[] targetSchemas = ResolveCompletionSchemas(profile, schemas, normalizedPreferredSchema);
        LocalizationDictionarySnapshot localizationSnapshot = await _localizationResolver.GetSnapshotAsync(profile, targetSchemas, cancellationToken);
        List<CompletionEntry> items = [];

        foreach (string schema in targetSchemas)
        {
            IReadOnlyDictionary<string, string> objectComments = await GetObjectCommentsAsync(connection, provider, profile, schema, cancellationToken);
            IReadOnlyDictionary<string, IReadOnlyList<string>> schemaColumns = await GetSchemaColumnsAsync(connection, provider, profile, schema, cancellationToken);
            IReadOnlyList<string> tables = await GetTablesAsync(connection, provider, profile, schema, cancellationToken);
            foreach (string table in tables)
            {
                IReadOnlyDictionary<string, string> columnComments = await GetColumnCommentsAsync(connection, provider, profile, schema, table, cancellationToken);
                items.Add(new CompletionEntry
                {
                    DisplayText = ResolveObjectCompletionDisplayName(localizationSnapshot, objectComments, schema, table, "table"),
                    InsertText = table,
                    Kind = "table",
                    Description = $"{schema}.table",
                    MatchKeys = BuildObjectCompletionMatchKeys(localizationSnapshot, objectComments, schema, table, "table"),
                    SourceObject = $"{schema}.{table}",
                    SortWeight = 200
                });

                foreach (string column in GetObjectColumns(schemaColumns, table))
                {
                    items.Add(new CompletionEntry
                    {
                        DisplayText = ResolveColumnCompletionDisplayName(localizationSnapshot, columnComments, schema, table, column),
                        InsertText = column,
                        Kind = "column",
                        Description = $"{schema}.{table}",
                        MatchKeys = BuildColumnCompletionMatchKeys(localizationSnapshot, columnComments, schema, table, column),
                        SourceObject = $"{schema}.{table}.{column}",
                        SortWeight = 300
                    });
                }
            }

            foreach (string view in await GetViewsAsync(connection, provider, profile, schema, cancellationToken))
            {
                IReadOnlyDictionary<string, string> columnComments = await GetColumnCommentsAsync(connection, provider, profile, schema, view, cancellationToken);
                items.Add(new CompletionEntry
                {
                    DisplayText = ResolveObjectCompletionDisplayName(localizationSnapshot, objectComments, schema, view, "view"),
                    InsertText = view,
                    Kind = "view",
                    Description = $"{schema}.view",
                    MatchKeys = BuildObjectCompletionMatchKeys(localizationSnapshot, objectComments, schema, view, "view"),
                    SourceObject = $"{schema}.{view}",
                    SortWeight = 220
                });

                foreach (string column in GetObjectColumns(schemaColumns, view))
                {
                    items.Add(new CompletionEntry
                    {
                        DisplayText = ResolveColumnCompletionDisplayName(localizationSnapshot, columnComments, schema, view, column),
                        InsertText = column,
                        Kind = "column",
                        Description = $"{schema}.{view}",
                        MatchKeys = BuildColumnCompletionMatchKeys(localizationSnapshot, columnComments, schema, view, column),
                        SourceObject = $"{schema}.{view}.{column}",
                        SortWeight = 320
                    });
                }
            }
        }

        IReadOnlyList<CompletionEntry> built = items
            .GroupBy(item => item.SourceObject, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        _completionCache[cacheKey] = built;
        return built;
    }
    public async Task<IReadOnlyList<CompletionEntry>> LoadRelationCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default)
    {
        string normalizedPreferredSchema = preferredSchema?.Trim() ?? string.Empty;
        string cacheKey = $"{profile.Id}:{profile.ProviderName}:{profile.Server}:{profile.Database}:{profile.Schema}:{normalizedPreferredSchema}";
        if (_relationCompletionCache.TryGetValue(cacheKey, out IReadOnlyList<CompletionEntry>? cached))
        {
            return cached;
        }

        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<CompletionEntry>();
        }

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        IReadOnlyList<string> schemas = await GetSchemasAsync(connection, provider, profile, cancellationToken);
        string[] targetSchemas = ResolveCompletionSchemas(profile, schemas, normalizedPreferredSchema);
        LocalizationDictionarySnapshot localizationSnapshot = await _localizationResolver.GetSnapshotAsync(profile, targetSchemas, cancellationToken);
        List<CompletionEntry> items = [];

        foreach (string schema in targetSchemas)
        {
            IReadOnlyDictionary<string, string> objectComments = await GetObjectCommentsAsync(connection, provider, profile, schema, cancellationToken);
            IReadOnlyList<string> tables = await GetTablesAsync(connection, provider, profile, schema, cancellationToken);
            foreach (string table in tables)
            {
                items.Add(new CompletionEntry
                {
                    DisplayText = ResolveObjectCompletionDisplayName(localizationSnapshot, objectComments, schema, table, "table"),
                    InsertText = table,
                    Kind = "table",
                    Description = $"{schema}.table",
                    MatchKeys = BuildObjectCompletionMatchKeys(localizationSnapshot, objectComments, schema, table, "table"),
                    SourceObject = $"{schema}.{table}",
                    SortWeight = 50
                });
            }

            IReadOnlyList<string> views = await GetViewsAsync(connection, provider, profile, schema, cancellationToken);
            foreach (string view in views)
            {
                items.Add(new CompletionEntry
                {
                    DisplayText = ResolveObjectCompletionDisplayName(localizationSnapshot, objectComments, schema, view, "view"),
                    InsertText = view,
                    Kind = "view",
                    Description = $"{schema}.view",
                    MatchKeys = BuildObjectCompletionMatchKeys(localizationSnapshot, objectComments, schema, view, "view"),
                    SourceObject = $"{schema}.{view}",
                    SortWeight = 60
                });
            }

            IReadOnlyList<string> materializedViews = await GetMaterializedViewsAsync(connection, provider, profile, schema, cancellationToken);
            foreach (string materializedView in materializedViews)
            {
                items.Add(new CompletionEntry
                {
                    DisplayText = ResolveObjectCompletionDisplayName(localizationSnapshot, objectComments, schema, materializedView, "materializedview"),
                    InsertText = materializedView,
                    Kind = "materializedview",
                    Description = $"{schema}.materializedview",
                    MatchKeys = BuildObjectCompletionMatchKeys(localizationSnapshot, objectComments, schema, materializedView, "materializedview"),
                    SourceObject = $"{schema}.{materializedView}",
                    SortWeight = 65
                });
            }

            IReadOnlyList<string> synonyms = await GetSynonymsAsync(connection, provider, profile, schema, cancellationToken);
            foreach (string synonym in synonyms)
            {
                items.Add(new CompletionEntry
                {
                    DisplayText = ResolveObjectCompletionDisplayName(localizationSnapshot, objectComments, schema, synonym, "synonym"),
                    InsertText = synonym,
                    Kind = "synonym",
                    Description = $"{schema}.synonym",
                    MatchKeys = BuildObjectCompletionMatchKeys(localizationSnapshot, objectComments, schema, synonym, "synonym"),
                    SourceObject = $"{schema}.{synonym}",
                    SortWeight = 70
                });
            }
        }

        IReadOnlyList<CompletionEntry> built = items
            .GroupBy(item => item.SourceObject, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        _relationCompletionCache[cacheKey] = built;
        return built;
    }
    public async Task<IReadOnlyList<CompletionEntry>> LoadObjectColumnCompletionSnapshotAsync(ConnectionProfile profile, string objectName, string? preferredSchema = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return Array.Empty<CompletionEntry>();
        }

        string normalizedPreferredSchema = preferredSchema?.Trim() ?? string.Empty;
        string normalizedObjectName = objectName.Trim();
        string cacheKey = $"{profile.Id}:{profile.ProviderName}:{profile.Server}:{profile.Database}:{profile.Schema}:{normalizedPreferredSchema}:columns:{normalizedObjectName}";
        if (_objectColumnCompletionCache.TryGetValue(cacheKey, out IReadOnlyList<CompletionEntry>? cached))
        {
            return cached;
        }

        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<CompletionEntry>();
        }

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        IReadOnlyList<string> schemas = await GetSchemasAsync(connection, provider, profile, cancellationToken);
        string[] targetSchemas = ResolveCompletionSchemas(profile, schemas, normalizedPreferredSchema);
        LocalizationDictionarySnapshot localizationSnapshot = await _localizationResolver.GetSnapshotAsync(profile, targetSchemas, cancellationToken);
        List<CompletionEntry> items = [];

        foreach (string schema in targetSchemas)
        {
            IReadOnlyDictionary<string, string> columnComments = await GetColumnCommentsAsync(connection, provider, profile, schema, normalizedObjectName, cancellationToken);
            IReadOnlyList<string> columns = await GetColumnsAsync(connection, provider, profile, schema, normalizedObjectName, cancellationToken);
            foreach (string column in columns)
            {
                items.Add(new CompletionEntry
                {
                    DisplayText = ResolveColumnCompletionDisplayName(localizationSnapshot, columnComments, schema, normalizedObjectName, column),
                    InsertText = column,
                    Kind = "column",
                    Description = $"{schema}.{normalizedObjectName}",
                    MatchKeys = BuildColumnCompletionMatchKeys(localizationSnapshot, columnComments, schema, normalizedObjectName, column),
                    SourceObject = $"{schema}.{normalizedObjectName}.{column}",
                    SortWeight = 300
                });
            }
        }

        IReadOnlyList<CompletionEntry> built = items
            .GroupBy(item => item.SourceObject, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        _objectColumnCompletionCache[cacheKey] = built;
        return built;
    }
    public async Task<IReadOnlyList<CompletionEntry>> SearchRelationCompletionEntriesAsync(
        ConnectionProfile profile,
        string prefix,
        string? preferredSchema = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Array.Empty<CompletionEntry>();
        }

        string normalizedPrefix = prefix.Trim();
        IReadOnlyList<CompletionEntry> snapshot = await LoadRelationCompletionSnapshotAsync(profile, preferredSchema, cancellationToken);
        return snapshot
            .Where(item => item.MatchKeys.Any(key =>
                key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                (normalizedPrefix.Length >= 2 && key.Contains(normalizedPrefix, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(item => item.SortWeight)
            .ThenBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToArray();
    }
    public void Clear(ConnectionProfile profile)
    {
        string prefix = $"{profile.ProviderName}|{profile.Server}|{profile.Database}|{profile.UserName}|";
        RemoveMatchingKeys(_schemaCache, prefix);
        RemoveMatchingKeys(_tableCache, prefix);
        RemoveMatchingKeys(_viewCache, prefix);
        RemoveMatchingKeys(_materializedViewCache, prefix);
        RemoveMatchingKeys(_synonymCache, prefix);
        RemoveMatchingKeys(_columnCache, prefix);
        RemoveMatchingKeys(_schemaColumnCache, prefix);
        RemoveMatchingKeys(_objectCommentCache, prefix);
        RemoveMatchingKeys(_columnCommentCache, prefix);

        string completionPrefix = $"{profile.Id}:{profile.ProviderName}:{profile.Server}:{profile.Database}:{profile.Schema}:";
        foreach (string key in _completionCache.Keys.Where(key => key.StartsWith(completionPrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _completionCache.TryRemove(key, out _);
        }

        foreach (string key in _relationCompletionCache.Keys.Where(key => key.StartsWith(completionPrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _relationCompletionCache.TryRemove(key, out _);
        }

        foreach (string key in _objectColumnCompletionCache.Keys.Where(key => key.StartsWith(completionPrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _objectColumnCompletionCache.TryRemove(key, out _);
        }
    }
    public void ClearAll()
    {
        _schemaCache.Clear();
        _tableCache.Clear();
        _viewCache.Clear();
        _materializedViewCache.Clear();
        _synonymCache.Clear();
        _columnCache.Clear();
        _schemaColumnCache.Clear();
        _objectCommentCache.Clear();
        _columnCommentCache.Clear();
        _completionCache.Clear();
        _relationCompletionCache.Clear();
        _objectColumnCompletionCache.Clear();
    }
    private async Task<IReadOnlyList<string>> GetSchemasAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, "schemas");
        if (_schemaCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string sql = provider.Name switch
        {
            "Oracle" => "select USERNAME from ALL_USERS order by USERNAME",
            "SqlServer" => "select name from sys.schemas order by name",
            "PostgreSql" => "select schema_name from information_schema.schemata order by schema_name",
            "MySql" or "MariaDB" => "select schema_name from information_schema.schemata order by schema_name",
            "KingbaseES" => "select schema_name from information_schema.schemata order by schema_name",
            "Dameng" => "select USERNAME from ALL_USERS order by USERNAME",
            "SQLite" => "select name from pragma_database_list order by case name when 'main' then 0 when 'temp' then 1 else 2 end, name",
            _ => string.Empty
        };

        IReadOnlyList<string> loaded = string.IsNullOrWhiteSpace(sql)
            ? Array.Empty<string>()
            : await ReadSingleColumnAsync(connection, sql, cancellationToken);

        _schemaCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetTablesAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:tables");
        if (_tableCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        IReadOnlyList<string> loaded = await ReadSingleColumnAsync(connection, BuildTablesSql(provider, schema), cancellationToken);
        _tableCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetViewsAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:views");
        if (_viewCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        IReadOnlyList<string> loaded = await ReadSingleColumnAsync(connection, BuildViewsSql(provider, schema), cancellationToken);
        _viewCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetMaterializedViewsAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:materializedviews");
        if (_materializedViewCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string sql = BuildMaterializedViewsSql(provider, schema);
        IReadOnlyList<string> loaded = string.IsNullOrWhiteSpace(sql)
            ? Array.Empty<string>()
            : await ReadSingleColumnAsync(connection, sql, cancellationToken);

        _materializedViewCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetSynonymsAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:synonyms");
        if (_synonymCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string sql = BuildSynonymsSql(provider, schema);
        IReadOnlyList<string> loaded = string.IsNullOrWhiteSpace(sql)
            ? Array.Empty<string>()
            : await ReadSingleColumnAsync(connection, sql, cancellationToken);

        _synonymCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetColumnsAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, string objectName, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:columns:{objectName}");
        if (_columnCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string schemaKey = BuildScopeKey(profile, $"{schema}:columns:*");
        if (_schemaColumnCache.TryGetValue(schemaKey, out IReadOnlyDictionary<string, IReadOnlyList<string>>? cachedSchemaColumns))
        {
            IReadOnlyList<string> cachedObjectColumns = GetObjectColumns(cachedSchemaColumns, objectName);
            _columnCache[key] = cachedObjectColumns;
            return cachedObjectColumns;
        }

        IReadOnlyList<string> loaded = await ReadSingleColumnAsync(connection, BuildColumnsSql(provider, schema, objectName), cancellationToken);
        _columnCache[key] = loaded;
        return loaded;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetSchemaColumnsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        ConnectionProfile profile,
        string schema,
        CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:columns:*");
        if (_schemaColumnCache.TryGetValue(key, out IReadOnlyDictionary<string, IReadOnlyList<string>>? cached))
        {
            return cached;
        }

        string sql = BuildSchemaColumnsSql(provider, schema);
        IReadOnlyDictionary<string, IReadOnlyList<string>> loaded = string.IsNullOrWhiteSpace(sql)
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            : await ReadGroupedColumnsAsync(connection, sql, cancellationToken);

        _schemaColumnCache[key] = loaded;
        foreach (KeyValuePair<string, IReadOnlyList<string>> pair in loaded)
        {
            _columnCache[BuildScopeKey(profile, $"{schema}:columns:{pair.Key}")] = pair.Value;
        }

        return loaded;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetObjectCommentsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        ConnectionProfile profile,
        string schema,
        CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:object-comments");
        if (_objectCommentCache.TryGetValue(key, out IReadOnlyDictionary<string, string>? cached))
        {
            return cached;
        }

        string sql = BuildObjectCommentsSql(provider, schema);
        IReadOnlyDictionary<string, string> loaded = string.IsNullOrWhiteSpace(sql)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await ReadObjectCommentsAsync(connection, sql, cancellationToken);

        _objectCommentCache[key] = loaded;
        return loaded;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetColumnCommentsAsync(
        DbConnection connection,
        DatabaseProviderDefinition provider,
        ConnectionProfile profile,
        string schema,
        string objectName,
        CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:column-comments:{objectName}");
        if (_columnCommentCache.TryGetValue(key, out IReadOnlyDictionary<string, string>? cached))
        {
            return cached;
        }

        string sql = BuildColumnCommentsSql(provider, schema, objectName);
        IReadOnlyDictionary<string, string> loaded = string.IsNullOrWhiteSpace(sql)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await ReadColumnCommentsAsync(connection, sql, cancellationToken);

        _columnCommentCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> ReadSingleColumnAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<string>();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<string> values = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            string? value = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ReadGroupedColumnsAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Dictionary<string, List<string>> grouped = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            string? objectName = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
            string? columnName = reader.IsDBNull(1) ? null : reader.GetValue(1)?.ToString();
            if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(columnName))
            {
                continue;
            }

            string normalizedObjectName = objectName.Trim();
            string normalizedColumnName = columnName.Trim();
            if (!grouped.TryGetValue(normalizedObjectName, out List<string>? columns))
            {
                columns = [];
                grouped[normalizedObjectName] = columns;
            }

            if (!columns.Contains(normalizedColumnName, StringComparer.OrdinalIgnoreCase))
            {
                columns.Add(normalizedColumnName);
            }
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadObjectCommentsAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            string objectName = ReadString(reader, "OBJECT_NAME");
            string objectType = NormalizeObjectType(ReadString(reader, "OBJECT_TYPE"));
            string comment = ReadString(reader, "COMMENTS");
            if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(comment))
            {
                continue;
            }

            comments[BuildObjectCommentKey(objectName, objectType)] = comment;
        }

        return comments;
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadColumnCommentsAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = ReadString(reader, "COLUMN_NAME");
            string comment = ReadString(reader, "COMMENTS");
            if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(comment))
            {
                continue;
            }

            comments[columnName] = comment;
        }

        return comments;
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
    private static void RemoveMatchingKeys<T>(ConcurrentDictionary<string, T> dictionary, string prefix)
    {
        foreach (string key in dictionary.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            dictionary.TryRemove(key, out _);
        }
    }
    private static string BuildScopeKey(ConnectionProfile profile, string suffix)
    {
        return $"{profile.ProviderName}|{profile.Server}|{profile.Database}|{profile.UserName}|{suffix}";
    }
    private static string[] ResolveCompletionSchemas(ConnectionProfile profile, IReadOnlyList<string> schemas, string preferredSchema)
    {
        if (schemas.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (string.Equals(preferredSchema, "*", StringComparison.OrdinalIgnoreCase))
        {
            return schemas.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(preferredSchema))
        {
            string? explicitlySelected = schemas.FirstOrDefault(item =>
                string.Equals(item, preferredSchema, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(explicitlySelected))
            {
                return [explicitlySelected];
            }
        }

        string database = profile.Database?.Trim() ?? string.Empty;
        string userName = profile.UserName?.Trim() ?? string.Empty;

        string? preferred = schemas.FirstOrDefault(item =>
            string.Equals(item, database, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item, userName, StringComparison.OrdinalIgnoreCase));

        return preferred == null ? schemas.Take(3).ToArray() : [preferred];
    }
    private static string BuildTablesSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(NormalizeCatalogIdentifier(provider, schema));
        return provider.Name switch
        {
            "Oracle" => $"select table_name from all_tables where owner = '{escaped}' order by table_name",
            "SqlServer" => $"select table_name from information_schema.tables where table_type = 'BASE TABLE' and table_schema = '{escaped}' order by table_name",
            "PostgreSql" => $"select table_name from information_schema.tables where table_type = 'BASE TABLE' and table_schema = '{escaped}' order by table_name",
            "MySql" or "MariaDB" => $"select table_name from information_schema.tables where table_type = 'BASE TABLE' and table_schema = '{escaped}' order by table_name",
            "KingbaseES" => $"select table_name from information_schema.tables where table_type = 'BASE TABLE' and table_schema = '{escaped}' order by table_name",
            "Dameng" => $"select table_name from all_tables where owner = '{escaped}' order by table_name",
            "SQLite" => $"select name from pragma_table_list where schema = '{escaped}' and type in ('table', 'virtual') and name not like 'sqlite_%' order by name",
            _ => string.Empty
        };
    }
    private static string BuildViewsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(NormalizeCatalogIdentifier(provider, schema));
        return provider.Name switch
        {
            "Oracle" => $"select view_name from all_views where owner = '{escaped}' order by view_name",
            "SqlServer" => $"select table_name from information_schema.views where table_schema = '{escaped}' order by table_name",
            "PostgreSql" => $"select table_name from information_schema.views where table_schema = '{escaped}' order by table_name",
            "MySql" or "MariaDB" => $"select table_name from information_schema.views where table_schema = '{escaped}' order by table_name",
            "KingbaseES" => $"select table_name from information_schema.views where table_schema = '{escaped}' order by table_name",
            "Dameng" => $"select view_name from all_views where owner = '{escaped}' order by view_name",
            "SQLite" => $"select name from pragma_table_list where schema = '{escaped}' and type = 'view' order by name",
            _ => string.Empty
        };
    }
    private static string BuildMaterializedViewsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(NormalizeCatalogIdentifier(provider, schema));
        return provider.Name switch
        {
            "Oracle" => $"select mview_name from all_mviews where owner = '{escaped}' order by mview_name",
            "PostgreSql" => $"select matviewname from pg_matviews where schemaname = '{escaped}' order by matviewname",
            "KingbaseES" => $"select matviewname from pg_matviews where schemaname = '{escaped}' order by matviewname",
            "Dameng" => string.Empty,
            _ => string.Empty
        };
    }

    private static string BuildObjectCommentsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        string escapedUpper = EscapeSqlLiteral(schema.ToUpperInvariant());
        return provider.Name switch
        {
            "Oracle" or "Dameng" => $@"
select table_name as OBJECT_NAME,
       table_type as OBJECT_TYPE,
       comments as COMMENTS
 from all_tab_comments
 where owner = '{escapedUpper}'
   and comments is not null",
            "SqlServer" => $@"
select o.name as OBJECT_NAME,
       case o.type when 'U' then 'table' when 'V' then 'view' else o.type end as OBJECT_TYPE,
       cast(ep.value as nvarchar(4000)) as COMMENTS
  from sys.objects o
  join sys.schemas s on s.schema_id = o.schema_id
  join sys.extended_properties ep
    on ep.major_id = o.object_id
   and ep.minor_id = 0
   and ep.name = 'MS_Description'
 where s.name = '{escaped}'
   and o.type in ('U', 'V')",
            "PostgreSql" or "KingbaseES" => $@"
select c.relname as OBJECT_NAME,
       case c.relkind when 'r' then 'table' when 'v' then 'view' when 'm' then 'materializedview' else c.relkind::text end as OBJECT_TYPE,
       pgd.description as COMMENTS
  from pg_catalog.pg_class c
  join pg_catalog.pg_namespace n on n.oid = c.relnamespace
  join pg_catalog.pg_description pgd on pgd.objoid = c.oid and pgd.objsubid = 0
 where n.nspname = '{escaped}'
   and c.relkind in ('r', 'v', 'm')
   and pgd.description is not null
   and pgd.description <> ''",
            "MySql" or "MariaDB" => $@"
select table_name as OBJECT_NAME,
       table_type as OBJECT_TYPE,
       table_comment as COMMENTS
  from information_schema.tables
 where table_schema = '{escaped}'
   and table_comment <> ''",
            _ => string.Empty
        };
    }

    private static string BuildColumnCommentsSql(DatabaseProviderDefinition provider, string schema, string objectName)
    {
        string escapedSchema = EscapeSqlLiteral(schema);
        string escapedSchemaUpper = EscapeSqlLiteral(schema.ToUpperInvariant());
        string escapedObject = EscapeSqlLiteral(objectName);
        string escapedObjectUpper = EscapeSqlLiteral(objectName.ToUpperInvariant());
        return provider.Name switch
        {
            "Oracle" or "Dameng" => $@"
select column_name as COLUMN_NAME,
       comments as COMMENTS
 from all_col_comments
 where owner = '{escapedSchemaUpper}'
   and table_name = '{escapedObjectUpper}'
   and comments is not null",
            "SqlServer" => $@"
select c.name as COLUMN_NAME,
       cast(ep.value as nvarchar(4000)) as COMMENTS
  from sys.columns c
  join sys.objects o on o.object_id = c.object_id
  join sys.schemas s on s.schema_id = o.schema_id
  join sys.extended_properties ep
    on ep.major_id = c.object_id
   and ep.minor_id = c.column_id
   and ep.name = 'MS_Description'
 where s.name = '{escapedSchema}'
   and o.name = '{escapedObject}'
   and o.type in ('U', 'V')
   and ep.value is not null",
            "PostgreSql" or "KingbaseES" => $@"
select a.attname as COLUMN_NAME,
       pgd.description as COMMENTS
  from pg_catalog.pg_class c
  join pg_catalog.pg_namespace n on n.oid = c.relnamespace
  join pg_catalog.pg_attribute a on a.attrelid = c.oid and a.attnum > 0 and not a.attisdropped
  join pg_catalog.pg_description pgd on pgd.objoid = c.oid and pgd.objsubid = a.attnum
 where n.nspname = '{escapedSchema}'
   and c.relname = '{escapedObject}'
   and c.relkind in ('r', 'v', 'm')
   and pgd.description is not null
   and pgd.description <> ''",
            "MySql" or "MariaDB" => $@"
select column_name as COLUMN_NAME,
       column_comment as COMMENTS
  from information_schema.columns
 where table_schema = '{escapedSchema}'
   and table_name = '{escapedObject}'
   and column_comment <> ''",
            _ => string.Empty
        };
    }
    private static string BuildSynonymsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(NormalizeCatalogIdentifier(provider, schema));
        return provider.Name switch
        {
            "Oracle" => $"select synonym_name from all_synonyms where owner = '{escaped}' order by synonym_name",
            "Dameng" => $"select synonym_name from all_synonyms where owner = '{escaped}' order by synonym_name",
            _ => string.Empty
        };
    }
    private static string BuildColumnsSql(DatabaseProviderDefinition provider, string schema, string objectName)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeCatalogIdentifier(provider, schema));
        string escapedObject = EscapeSqlLiteral(NormalizeCatalogIdentifier(provider, objectName));
        return provider.Name switch
        {
            "Oracle" => $"select column_name from all_tab_columns where owner = '{escapedSchema}' and table_name = '{escapedObject}' order by column_id",
            "SqlServer" => $"select column_name from information_schema.columns where table_schema = '{escapedSchema}' and table_name = '{escapedObject}' order by ordinal_position",
            "PostgreSql" => $"select column_name from information_schema.columns where table_schema = '{escapedSchema}' and table_name = '{escapedObject}' order by ordinal_position",
            "MySql" or "MariaDB" => $"select column_name from information_schema.columns where table_schema = '{escapedSchema}' and table_name = '{escapedObject}' order by ordinal_position",
            "KingbaseES" => $"select column_name from information_schema.columns where table_schema = '{escapedSchema}' and table_name = '{escapedObject}' order by ordinal_position",
            "Dameng" => $"select column_name from all_tab_columns where owner = '{escapedSchema}' and table_name = '{escapedObject}' order by column_id",
            "SQLite" => $"select name from pragma_table_info('{escapedObject}') order by cid",
            _ => string.Empty
        };
    }

    private static string BuildSchemaColumnsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escapedSchema = EscapeSqlLiteral(NormalizeCatalogIdentifier(provider, schema));
        return provider.Name switch
        {
            "Oracle" => $"select table_name, column_name from all_tab_columns where owner = '{escapedSchema}' order by table_name, column_id",
            "SqlServer" => $"select table_name, column_name from information_schema.columns where table_schema = '{escapedSchema}' order by table_name, ordinal_position",
            "PostgreSql" => $"select table_name, column_name from information_schema.columns where table_schema = '{escapedSchema}' order by table_name, ordinal_position",
            "MySql" or "MariaDB" => $"select table_name, column_name from information_schema.columns where table_schema = '{escapedSchema}' order by table_name, ordinal_position",
            "KingbaseES" => $"select table_name, column_name from information_schema.columns where table_schema = '{escapedSchema}' order by table_name, ordinal_position",
            "Dameng" => $"select table_name, column_name from all_tab_columns where owner = '{escapedSchema}' order by table_name, column_id",
            "SQLite" => $"select t.name as table_name, p.name as column_name from pragma_table_list t join pragma_table_info(t.name) p where t.schema = '{escapedSchema}' and t.type in ('table', 'view', 'virtual') and t.name not like 'sqlite_%' order by t.name, p.cid",
            _ => string.Empty
        };
    }

    private static IReadOnlyList<string> GetObjectColumns(IReadOnlyDictionary<string, IReadOnlyList<string>> schemaColumns, string objectName)
    {
        return schemaColumns.TryGetValue(objectName, out IReadOnlyList<string>? columns)
            ? columns
            : Array.Empty<string>();
    }

    private string ResolveObjectCompletionDisplayName(
        LocalizationDictionarySnapshot localizationSnapshot,
        IReadOnlyDictionary<string, string> comments,
        string schema,
        string objectName,
        string objectType)
    {
        string resolved = _localizationResolver.ResolveObjectDisplayName(localizationSnapshot, schema, objectName, objectType, objectName);
        if (!string.Equals(resolved, objectName, StringComparison.OrdinalIgnoreCase))
        {
            return resolved;
        }

        return TryGetObjectComment(comments, objectName, objectType, out string? comment) ? comment ?? objectName : objectName;
    }

    private IReadOnlyList<string> BuildObjectCompletionMatchKeys(
        LocalizationDictionarySnapshot localizationSnapshot,
        IReadOnlyDictionary<string, string> comments,
        string schema,
        string objectName,
        string objectType)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in _localizationResolver.BuildObjectMatchKeys(localizationSnapshot, schema, objectName, objectType, BuildMatchKeys(schema, objectName)))
        {
            AddMatchValue(keys, key);
        }

        if (TryGetObjectComment(comments, objectName, objectType, out string? comment))
        {
            AddMatchValue(keys, comment);
        }

        return keys.ToArray();
    }

    private string ResolveColumnCompletionDisplayName(
        LocalizationDictionarySnapshot localizationSnapshot,
        IReadOnlyDictionary<string, string> comments,
        string schema,
        string objectName,
        string columnName)
    {
        string resolved = _localizationResolver.ResolveColumnDisplayName(localizationSnapshot, schema, objectName, columnName, columnName);
        if (!string.Equals(resolved, columnName, StringComparison.OrdinalIgnoreCase))
        {
            return resolved;
        }

        return comments.TryGetValue(columnName, out string? comment) && !string.IsNullOrWhiteSpace(comment)
            ? comment
            : columnName;
    }

    private IReadOnlyList<string> BuildColumnCompletionMatchKeys(
        LocalizationDictionarySnapshot localizationSnapshot,
        IReadOnlyDictionary<string, string> comments,
        string schema,
        string objectName,
        string columnName)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in _localizationResolver.BuildColumnMatchKeys(localizationSnapshot, schema, objectName, columnName, BuildMatchKeys(schema, objectName, columnName)))
        {
            AddMatchValue(keys, key);
        }

        if (comments.TryGetValue(columnName, out string? comment))
        {
            AddMatchValue(keys, comment);
        }

        return keys.ToArray();
    }

    private static bool TryGetObjectComment(
        IReadOnlyDictionary<string, string> comments,
        string objectName,
        string objectType,
        out string? comment)
    {
        if (comments.TryGetValue(BuildObjectCommentKey(objectName, objectType), out comment) && !string.IsNullOrWhiteSpace(comment))
        {
            return true;
        }

        return comments.TryGetValue(BuildObjectCommentKey(objectName, string.Empty), out comment) && !string.IsNullOrWhiteSpace(comment);
    }

    private static string BuildObjectCommentKey(string objectName, string objectType)
    {
        return string.Concat(NormalizeObjectType(objectType), "\u001f", objectName.Trim());
    }

    private static void AddMatchValue(HashSet<string> keys, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string trimmed = value.Trim();
        keys.Add(trimmed);

        string compact = new(trimmed.Where(static ch => !char.IsWhiteSpace(ch)).ToArray());
        if (!string.Equals(compact, trimmed, StringComparison.Ordinal))
        {
            keys.Add(compact);
        }
    }

    private static string ReadString(DbDataReader reader, string name)
    {
        object? value = reader[name];
        return value == null || value == DBNull.Value ? string.Empty : value.ToString()?.Trim() ?? string.Empty;
    }

    private static string NormalizeObjectType(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "table" or "base table" => "table",
            "view" => "view",
            "materialized view" or "materializedview" => "materializedview",
            "synonym" => "synonym",
            _ => (value ?? string.Empty).Trim().ToLowerInvariant()
        };
    }
    private static IReadOnlyList<string> BuildMatchKeys(string schema, string objectName, string? memberName = null)
    {
        List<string> keys = [objectName];
        if (!string.IsNullOrWhiteSpace(schema))
        {
            keys.Add($"{schema}.{objectName}");
        }

        if (!string.IsNullOrWhiteSpace(memberName))
        {
            keys.Add(memberName);
            keys.Add($"{objectName}.{memberName}");
            if (!string.IsNullOrWhiteSpace(schema))
            {
                keys.Add($"{schema}.{objectName}.{memberName}");
            }
        }

        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private static string NormalizeCatalogIdentifier(DatabaseProviderDefinition provider, string value)
    {
        string normalized = value.Trim();
        return provider.Name is "Oracle" or "Dameng"
            ? normalized.ToUpperInvariant()
            : normalized;
    }
}
