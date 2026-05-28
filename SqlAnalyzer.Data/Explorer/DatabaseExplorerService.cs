using System.Collections.Concurrent;
using System.Data.Common;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;

public sealed class DatabaseExplorerService : IDatabaseExplorerService
{
    private readonly DbProviderRuntime _runtime;
    private readonly ILocalizationResolver _localizationResolver;
    private readonly ICompletionMetadataService _completionMetadataService;
    private readonly ExplorerMetadataService _explorerMetadataService;
    private readonly ITableDesignService _tableDesignService;
    private readonly IConnectionValidationService _connectionValidationService;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _schemaCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _tableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _viewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _materializedViewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _functionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _procedureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _sequenceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _triggerCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _synonymCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _packageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _columnCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _columnCommentCache = new(StringComparer.OrdinalIgnoreCase);
    public DatabaseExplorerService(IDatabaseProviderCatalog providerCatalog, ILocalizationResolver localizationResolver)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
        _localizationResolver = localizationResolver;
        _completionMetadataService = new CompletionMetadataService(providerCatalog, _localizationResolver);
        _explorerMetadataService = new ExplorerMetadataService(_localizationResolver);
        _tableDesignService = new TableDesignService(providerCatalog);
        _connectionValidationService = new ConnectionValidationService(providerCatalog);
    }
    public Task<IReadOnlyList<ObjectNode>> LoadRootNodesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        return _explorerMetadataService.LoadRootNodesAsync(profile, provider);
    }
    public async Task<IReadOnlyList<ObjectNode>> LoadChildNodesAsync(ConnectionProfile profile, ObjectNode node, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ObjectNode>();
        }

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        return await _explorerMetadataService.LoadChildNodesAsync(
            profile,
            node,
            provider,
            token => GetSchemasAsync(connection, provider, profile, token),
            (schema, token) => GetTablesAsync(connection, provider, profile, schema, token),
            (schema, token) => GetViewsAsync(connection, provider, profile, schema, token),
            (schema, token) => GetMaterializedViewsAsync(connection, provider, profile, schema, token),
            (schema, token) => GetFunctionsAsync(connection, provider, profile, schema, token),
            (schema, token) => GetProceduresAsync(connection, provider, profile, schema, token),
            (schema, token) => GetSequencesAsync(connection, provider, profile, schema, token),
            (schema, token) => GetTriggersAsync(connection, provider, profile, schema, token),
            (schema, token) => GetSynonymsAsync(connection, provider, profile, schema, token),
            (schema, token) => GetPackagesAsync(connection, provider, profile, schema, token),
            cancellationToken);
    }
    public async Task<IReadOnlyList<string>> LoadCompletionItemsAsync(ConnectionProfile profile, string prefix, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<CompletionEntry> snapshot = await LoadCompletionSnapshotAsync(profile, null, cancellationToken);
        return snapshot
            .Where(item => item.MatchKeys.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.SortWeight)
            .ThenBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.InsertText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();
    }
    public IReadOnlyList<CompletionEntry> GetCachedCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null)
    {
        return _completionMetadataService.GetCachedCompletionSnapshot(profile, preferredSchema);
    }
    public IReadOnlyList<CompletionEntry> GetCachedRelationCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null)
    {
        return _completionMetadataService.GetCachedRelationCompletionSnapshot(profile, preferredSchema);
    }
    public IReadOnlyList<CompletionEntry> GetCachedObjectColumnCompletionSnapshot(ConnectionProfile profile, string objectName, string? preferredSchema = null)
    {
        return _completionMetadataService.GetCachedObjectColumnCompletionSnapshot(profile, objectName, preferredSchema);
    }
    public async Task<IReadOnlyList<CompletionEntry>> LoadCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default)
    {
        return await _completionMetadataService.LoadCompletionSnapshotAsync(profile, preferredSchema, cancellationToken);
    }
    public async Task<IReadOnlyList<CompletionEntry>> LoadRelationCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default)
    {
        return await _completionMetadataService.LoadRelationCompletionSnapshotAsync(profile, preferredSchema, cancellationToken);
    }
    public async Task<IReadOnlyList<CompletionEntry>> LoadObjectColumnCompletionSnapshotAsync(ConnectionProfile profile, string objectName, string? preferredSchema = null, CancellationToken cancellationToken = default)
    {
        return await _completionMetadataService.LoadObjectColumnCompletionSnapshotAsync(profile, objectName, preferredSchema, cancellationToken);
    }
    public async Task<IReadOnlyList<CompletionEntry>> SearchRelationCompletionEntriesAsync(
        ConnectionProfile profile,
        string prefix,
        string? preferredSchema = null,
        CancellationToken cancellationToken = default)
    {
        return await _completionMetadataService.SearchRelationCompletionEntriesAsync(profile, prefix, preferredSchema, cancellationToken);
    }
    public async Task<IReadOnlyList<string>> LoadSchemasAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        return await GetSchemasAsync(connection, provider, profile, cancellationToken);
    }
    public async Task<IReadOnlyDictionary<string, string>> LoadColumnCommentLookupAsync(
        ConnectionProfile profile,
        string schemaName,
        IReadOnlyList<string> columnNames,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || columnNames.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        string normalizedColumnKey = string.Join("|", columnNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(normalizedColumnKey))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        string key = BuildScopeKey(profile, $"{schemaName}:column-comments:{normalizedColumnKey}");
        if (_columnCommentCache.TryGetValue(key, out IReadOnlyDictionary<string, string>? cached))
        {
            return cached;
        }

        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        string sql = BuildColumnCommentsSql(provider, schemaName, columnNames);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> duplicates = new(StringComparer.OrdinalIgnoreCase);
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader["COLUMN_NAME"]?.ToString()?.Trim() ?? string.Empty;
            string comment = reader["COMMENTS"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(comment))
            {
                continue;
            }

            if (comments.TryGetValue(columnName, out string? existing) && !string.Equals(existing, comment, StringComparison.Ordinal))
            {
                duplicates.Add(columnName);
                comments.Remove(columnName);
                continue;
            }

            if (!duplicates.Contains(columnName))
            {
                comments[columnName] = comment;
            }
        }

        _columnCommentCache[key] = comments;
        return comments;
    }
    public async Task<IReadOnlyDictionary<string, string>> LoadColumnDisplayNameLookupAsync(
        ConnectionProfile profile,
        string schemaName,
        IReadOnlyList<string> columnNames,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || columnNames.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        LocalizationDictionarySnapshot snapshot = await _localizationResolver.GetSnapshotAsync(profile, cancellationToken: cancellationToken);
        Dictionary<string, string> displayNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> duplicates = new(StringComparer.OrdinalIgnoreCase);

        foreach (LocalizedColumnLabel label in snapshot.ColumnLabels.Where(item =>
                     item.IsEnabled &&
                     string.Equals(item.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(item.ColumnName) &&
                     !string.IsNullOrWhiteSpace(item.DisplayName) &&
                     columnNames.Contains(item.ColumnName, StringComparer.OrdinalIgnoreCase)))
        {
            string columnName = label.ColumnName.Trim();
            string displayName = label.DisplayName.Trim();

            if (displayNames.TryGetValue(columnName, out string? existing) && !string.Equals(existing, displayName, StringComparison.Ordinal))
            {
                duplicates.Add(columnName);
                displayNames.Remove(columnName);
                continue;
            }

            if (!duplicates.Contains(columnName))
            {
                displayNames[columnName] = displayName;
            }
        }

        return displayNames;
    }

    // 结果列元数据会优先按“schema + table + column”精确解析本地化；只有缺少来源表时才退回到保守的唯一列名匹配。
    public async Task<IReadOnlyDictionary<string, ResultColumnMetadataEntry>> LoadResultColumnMetadataLookupAsync(
        ConnectionProfile profile,
        string schemaName,
        IReadOnlyList<ResultColumnMetadataRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || requests.Count == 0)
        {
            return new Dictionary<string, ResultColumnMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        }

        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        LocalizationDictionarySnapshot snapshot = await _localizationResolver.GetSnapshotAsync(profile, cancellationToken: cancellationToken);
        string[] columnNames = requests
            .Select(request => string.IsNullOrWhiteSpace(request.SourceColumn) ? request.RawName : request.SourceColumn ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "Message", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyDictionary<string, string> comments = columnNames.Length == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await LoadColumnCommentLookupAsync(profile, schemaName, columnNames, cancellationToken);
        IReadOnlyDictionary<string, string> exactComments = await LoadExactResultColumnCommentLookupAsync(
            profile,
            provider,
            schemaName,
            requests,
            cancellationToken);

        Dictionary<string, ResultColumnMetadataEntry> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (ResultColumnMetadataRequest request in requests)
        {
            string rawName = string.IsNullOrWhiteSpace(request.RawName) ? string.Empty : request.RawName.Trim();
            string effectiveSchema = string.IsNullOrWhiteSpace(request.SourceSchema) ? schemaName : request.SourceSchema!.Trim();
            string sourceColumn = string.IsNullOrWhiteSpace(request.SourceColumn) ? rawName : request.SourceColumn!.Trim();
            string? sourceTable = string.IsNullOrWhiteSpace(request.SourceTable) ? null : request.SourceTable!.Trim();

            string displayName = string.IsNullOrWhiteSpace(sourceTable)
                ? ResolveUniqueColumnDisplayName(snapshot, effectiveSchema, sourceColumn, rawName)
                : _localizationResolver.ResolveColumnDisplayName(snapshot, effectiveSchema, sourceTable, sourceColumn, rawName);
            string? commentText = null;
            if (!string.IsNullOrWhiteSpace(sourceTable))
            {
                exactComments.TryGetValue(BuildSchemaTableColumnKey(effectiveSchema, sourceTable, sourceColumn), out commentText);
            }

            if (string.IsNullOrWhiteSpace(commentText))
            {
                comments.TryGetValue(sourceColumn, out commentText);
            }

            if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(commentText))
            {
                continue;
            }

            result[request.Key] = new ResultColumnMetadataEntry
            {
                DisplayName = displayName,
                CommentText = commentText ?? string.Empty
            };
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadExactResultColumnCommentLookupAsync(
        ConnectionProfile profile,
        DatabaseProviderDefinition provider,
        string fallbackSchemaName,
        IReadOnlyList<ResultColumnMetadataRequest> requests,
        CancellationToken cancellationToken)
    {
        var exactRequests = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.SourceTable) && !string.IsNullOrWhiteSpace(request.SourceColumn))
            .Select(request => new
            {
                SchemaName = string.IsNullOrWhiteSpace(request.SourceSchema) ? fallbackSchemaName : request.SourceSchema!.Trim(),
                TableName = request.SourceTable!.Trim(),
                ColumnName = request.SourceColumn!.Trim()
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.SchemaName))
            .Distinct()
            .ToArray();
        if (exactRequests.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);
        await using DbConnection connection = await OpenConnectionAsync(provider, profile, cancellationToken);
        foreach (var schemaGroup in exactRequests.GroupBy(item => item.SchemaName, StringComparer.OrdinalIgnoreCase))
        {
            string sql = BuildExactResultColumnCommentsSql(
                provider,
                schemaGroup.Key,
                schemaGroup.Select(item => item.TableName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                schemaGroup.Select(item => item.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            if (string.IsNullOrWhiteSpace(sql))
            {
                continue;
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string tableName = reader["TABLE_NAME"]?.ToString()?.Trim() ?? string.Empty;
                string columnName = reader["COLUMN_NAME"]?.ToString()?.Trim() ?? string.Empty;
                string comment = reader["COMMENTS"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(comment))
                {
                    continue;
                }

                comments[BuildSchemaTableColumnKey(schemaGroup.Key, tableName, columnName)] = comment;
            }
        }

        return comments;
    }

    // 仅当当前模式下同名列只有唯一一个显示名时才允许回填，避免把高重复字段误标到错误的业务名称上。
    private static string ResolveUniqueColumnDisplayName(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string columnName,
        string fallbackDisplayName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return fallbackDisplayName;
        }

        string[] displayNames = snapshot.ColumnLabels
            .Where(item =>
                item.IsEnabled &&
                string.Equals(item.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ColumnName, columnName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.DisplayName))
            .Select(item => item.DisplayName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return displayNames.Length == 1 ? displayNames[0] : fallbackDisplayName;
    }

    private static string BuildSchemaTableColumnKey(string schemaName, string tableName, string columnName)
    {
        return string.Concat(schemaName.Trim(), "\u001f", tableName.Trim(), "\u001f", columnName.Trim());
    }
    public async Task<TableDesignModel> LoadTableDesignAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        return await _tableDesignService.LoadTableDesignAsync(profile, schemaName, tableName, cancellationToken);
    }
    public async Task<string> ExportTableStructureAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        return await _tableDesignService.ExportTableStructureAsync(profile, schemaName, tableName, cancellationToken);
    }
    public async Task<string> ExportTableDataAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        return await _tableDesignService.ExportTableDataAsync(profile, schemaName, tableName, cancellationToken);
    }
    public async Task SaveTableDesignAsync(ConnectionProfile profile, TableDesignModel originalDesign, TableDesignModel updatedDesign, CancellationToken cancellationToken = default)
    {
        await _tableDesignService.SaveTableDesignAsync(profile, originalDesign, updatedDesign, cancellationToken);
        ClearMetadataCaches();
    }
    public async Task<string> ValidateConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        return await _connectionValidationService.ValidateConnectionAsync(profile, cancellationToken);
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
            "MySql" => "select schema_name from information_schema.schemata order by schema_name",
            "KingbaseES" => "select schema_name from information_schema.schemata order by schema_name",
            "Dameng" => "select USERNAME from ALL_USERS order by USERNAME",
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
    private async Task<IReadOnlyList<string>> GetFunctionsAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:functions");
        if (_functionCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        IReadOnlyList<string> loaded = await ReadSingleColumnAsync(connection, BuildFunctionsSql(provider, schema), cancellationToken);
        _functionCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetProceduresAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:procedures");
        if (_procedureCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string sql = BuildProceduresSql(provider, schema);
        IReadOnlyList<string> loaded = string.IsNullOrWhiteSpace(sql)
            ? Array.Empty<string>()
            : await ReadSingleColumnAsync(connection, sql, cancellationToken);

        _procedureCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetSequencesAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:sequences");
        if (_sequenceCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string sql = BuildSequencesSql(provider, schema);
        IReadOnlyList<string> loaded = string.IsNullOrWhiteSpace(sql)
            ? Array.Empty<string>()
            : await ReadSingleColumnAsync(connection, sql, cancellationToken);

        _sequenceCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetTriggersAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:triggers");
        if (_triggerCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string sql = BuildTriggersSql(provider, schema);
        IReadOnlyList<string> loaded = string.IsNullOrWhiteSpace(sql)
            ? Array.Empty<string>()
            : await ReadSingleColumnAsync(connection, sql, cancellationToken);

        _triggerCache[key] = loaded;
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
    private async Task<IReadOnlyList<string>> GetPackagesAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:packages");
        if (_packageCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string sql = BuildPackagesSql(provider, schema);
        IReadOnlyList<string> loaded = string.IsNullOrWhiteSpace(sql)
            ? Array.Empty<string>()
            : await ReadSingleColumnAsync(connection, sql, cancellationToken);

        _packageCache[key] = loaded;
        return loaded;
    }
    private async Task<IReadOnlyList<string>> GetColumnsAsync(DbConnection connection, DatabaseProviderDefinition provider, ConnectionProfile profile, string schema, string objectName, CancellationToken cancellationToken)
    {
        string key = BuildScopeKey(profile, $"{schema}:columns:{objectName}");
        if (_columnCache.TryGetValue(key, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        IReadOnlyList<string> loaded = await ReadSingleColumnAsync(connection, BuildColumnsSql(provider, schema, objectName), cancellationToken);
        _columnCache[key] = loaded;
        return loaded;
    }
    private static string BuildTablesSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "SqlServer" => $"select TABLE_NAME from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA = '{escaped}' and TABLE_TYPE = 'BASE TABLE' order by TABLE_NAME",
            "MySql" => $"select TABLE_NAME from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA = '{escaped}' and TABLE_TYPE = 'BASE TABLE' order by TABLE_NAME",
            "Oracle" => $"select TABLE_NAME from ALL_TABLES where OWNER = '{escaped.ToUpperInvariant()}' order by TABLE_NAME",
            "PostgreSql" => $"select TABLE_NAME from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA = '{escaped}' and TABLE_TYPE = 'BASE TABLE' order by TABLE_NAME",
            "KingbaseES" => $"select TABLE_NAME from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA = '{escaped}' and TABLE_TYPE = 'BASE TABLE' order by TABLE_NAME",
            "Dameng" => $"select TABLE_NAME from ALL_TABLES where OWNER = '{escaped.ToUpperInvariant()}' order by TABLE_NAME",
            _ => string.Empty
        };
    }
    private static string BuildViewsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "SqlServer" => $"select TABLE_NAME from INFORMATION_SCHEMA.VIEWS where TABLE_SCHEMA = '{escaped}' order by TABLE_NAME",
            "MySql" => $"select TABLE_NAME from INFORMATION_SCHEMA.VIEWS where TABLE_SCHEMA = '{escaped}' order by TABLE_NAME",
            "Oracle" => $"select VIEW_NAME from ALL_VIEWS where OWNER = '{escaped.ToUpperInvariant()}' order by VIEW_NAME",
            "PostgreSql" => $"select TABLE_NAME from INFORMATION_SCHEMA.VIEWS where TABLE_SCHEMA = '{escaped}' order by TABLE_NAME",
            "KingbaseES" => $"select TABLE_NAME from INFORMATION_SCHEMA.VIEWS where TABLE_SCHEMA = '{escaped}' order by TABLE_NAME",
            "Dameng" => $"select VIEW_NAME from ALL_VIEWS where OWNER = '{escaped.ToUpperInvariant()}' order by VIEW_NAME",
            _ => string.Empty
        };
    }
    private static string BuildMaterializedViewsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "Oracle" => $"select MVIEW_NAME from ALL_MVIEWS where OWNER = '{escaped.ToUpperInvariant()}' order by MVIEW_NAME",
            "PostgreSql" => $"select matviewname from pg_matviews where schemaname = '{escaped}' order by matviewname",
            "KingbaseES" => $"select matviewname from pg_matviews where schemaname = '{escaped}' order by matviewname",
            "SqlServer" => $"select v.name from sys.views v inner join sys.schemas s on v.schema_id = s.schema_id where s.name = '{escaped}' and objectproperty(v.object_id, 'IsIndexed') = 1 order by v.name",
            _ => string.Empty
        };
    }
    private static string BuildFunctionsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "SqlServer" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'FUNCTION' order by ROUTINE_NAME",
            "MySql" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'FUNCTION' order by ROUTINE_NAME",
            "Oracle" => $"select OBJECT_NAME from ALL_OBJECTS where OWNER = '{escaped.ToUpperInvariant()}' and OBJECT_TYPE = 'FUNCTION' order by OBJECT_NAME",
            "PostgreSql" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'FUNCTION' order by ROUTINE_NAME",
            "KingbaseES" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'FUNCTION' order by ROUTINE_NAME",
            "Dameng" => $"select OBJECT_NAME from ALL_OBJECTS where OWNER = '{escaped.ToUpperInvariant()}' and OBJECT_TYPE = 'FUNCTION' order by OBJECT_NAME",
            _ => string.Empty
        };
    }
    private static string BuildProceduresSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "SqlServer" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'PROCEDURE' order by ROUTINE_NAME",
            "MySql" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'PROCEDURE' order by ROUTINE_NAME",
            "Oracle" => $"select OBJECT_NAME from ALL_OBJECTS where OWNER = '{escaped.ToUpperInvariant()}' and OBJECT_TYPE = 'PROCEDURE' order by OBJECT_NAME",
            "PostgreSql" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'PROCEDURE' order by ROUTINE_NAME",
            "KingbaseES" => $"select ROUTINE_NAME from INFORMATION_SCHEMA.ROUTINES where ROUTINE_SCHEMA = '{escaped}' and ROUTINE_TYPE = 'PROCEDURE' order by ROUTINE_NAME",
            "Dameng" => $"select OBJECT_NAME from ALL_OBJECTS where OWNER = '{escaped.ToUpperInvariant()}' and OBJECT_TYPE = 'PROCEDURE' order by OBJECT_NAME",
            _ => string.Empty
        };
    }
    private static string BuildSequencesSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "Oracle" => $"select SEQUENCE_NAME from ALL_SEQUENCES where SEQUENCE_OWNER = '{escaped.ToUpperInvariant()}' order by SEQUENCE_NAME",
            "PostgreSql" => $"select sequence_name from information_schema.sequences where sequence_schema = '{escaped}' order by sequence_name",
            "KingbaseES" => $"select sequence_name from information_schema.sequences where sequence_schema = '{escaped}' order by sequence_name",
            "Dameng" => $"select SEQUENCE_NAME from ALL_SEQUENCES where SEQUENCE_OWNER = '{escaped.ToUpperInvariant()}' order by SEQUENCE_NAME",
            _ => string.Empty
        };
    }
    private static string BuildTriggersSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "SqlServer" => $"select t.name from sys.triggers t inner join sys.objects o on t.parent_id = o.object_id inner join sys.schemas s on o.schema_id = s.schema_id where s.name = '{escaped}' order by t.name",
            "MySql" => $"select TRIGGER_NAME from INFORMATION_SCHEMA.TRIGGERS where TRIGGER_SCHEMA = '{escaped}' order by TRIGGER_NAME",
            "Oracle" => $"select TRIGGER_NAME from ALL_TRIGGERS where OWNER = '{escaped.ToUpperInvariant()}' order by TRIGGER_NAME",
            "PostgreSql" => $"select trigger_name from information_schema.triggers where trigger_schema = '{escaped}' order by trigger_name",
            "KingbaseES" => $"select trigger_name from information_schema.triggers where trigger_schema = '{escaped}' order by trigger_name",
            "Dameng" => $"select TRIGGER_NAME from ALL_TRIGGERS where OWNER = '{escaped.ToUpperInvariant()}' order by TRIGGER_NAME",
            _ => string.Empty
        };
    }
    private static string BuildSynonymsSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "Oracle" => $"select SYNONYM_NAME from ALL_SYNONYMS where OWNER = '{escaped.ToUpperInvariant()}' order by SYNONYM_NAME",
            "Dameng" => $"select SYNONYM_NAME from ALL_SYNONYMS where OWNER = '{escaped.ToUpperInvariant()}' order by SYNONYM_NAME",
            _ => string.Empty
        };
    }
    private static string BuildPackagesSql(DatabaseProviderDefinition provider, string schema)
    {
        string escaped = EscapeSqlLiteral(schema);
        return provider.Name switch
        {
            "Oracle" => $"select OBJECT_NAME from ALL_OBJECTS where OWNER = '{escaped.ToUpperInvariant()}' and OBJECT_TYPE = 'PACKAGE' order by OBJECT_NAME",
            _ => string.Empty
        };
    }
    private static string BuildColumnsSql(DatabaseProviderDefinition provider, string schema, string objectName)
    {
        string escapedSchema = EscapeSqlLiteral(schema);
        string escapedObject = EscapeSqlLiteral(objectName);
        return provider.Name switch
        {
            "SqlServer" => $"select COLUMN_NAME from INFORMATION_SCHEMA.COLUMNS where TABLE_SCHEMA = '{escapedSchema}' and TABLE_NAME = '{escapedObject}' order by ORDINAL_POSITION",
            "MySql" => $"select COLUMN_NAME from INFORMATION_SCHEMA.COLUMNS where TABLE_SCHEMA = '{escapedSchema}' and TABLE_NAME = '{escapedObject}' order by ORDINAL_POSITION",
            "Oracle" => $"select COLUMN_NAME from ALL_TAB_COLUMNS where OWNER = '{escapedSchema.ToUpperInvariant()}' and TABLE_NAME = '{escapedObject.ToUpperInvariant()}' order by COLUMN_ID",
            "PostgreSql" => $"select COLUMN_NAME from INFORMATION_SCHEMA.COLUMNS where TABLE_SCHEMA = '{escapedSchema}' and TABLE_NAME = '{escapedObject}' order by ORDINAL_POSITION",
            "KingbaseES" => $"select COLUMN_NAME from INFORMATION_SCHEMA.COLUMNS where TABLE_SCHEMA = '{escapedSchema}' and TABLE_NAME = '{escapedObject}' order by ORDINAL_POSITION",
            "Dameng" => $"select COLUMN_NAME from ALL_TAB_COLUMNS where OWNER = '{escapedSchema.ToUpperInvariant()}' and TABLE_NAME = '{escapedObject.ToUpperInvariant()}' order by COLUMN_ID",
            _ => string.Empty
        };
    }
    private static string BuildColumnCommentsSql(DatabaseProviderDefinition provider, string schema, IReadOnlyList<string> columnNames)
    {
        string escapedSchema = EscapeSqlLiteral(schema);
        string[] normalizedColumns = columnNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedColumns.Length == 0)
        {
            return string.Empty;
        }

        return provider.Name switch
        {
            "Oracle" => $@"select COLUMN_NAME, COMMENTS from ALL_COL_COMMENTS where OWNER = '{escapedSchema.ToUpperInvariant()}' and COMMENTS is not null and COLUMN_NAME in ({BuildSqlInList(normalizedColumns.Select(item => item.ToUpperInvariant()))})",
            "Dameng" => $@"select COLUMN_NAME, COMMENTS from ALL_COL_COMMENTS where OWNER = '{escapedSchema.ToUpperInvariant()}' and COMMENTS is not null and COLUMN_NAME in ({BuildSqlInList(normalizedColumns.Select(item => item.ToUpperInvariant()))})",
            "MySql" => $@"select COLUMN_NAME, COLUMN_COMMENT as COMMENTS from INFORMATION_SCHEMA.COLUMNS where TABLE_SCHEMA = '{escapedSchema}' and COLUMN_COMMENT <> '' and COLUMN_NAME in ({BuildSqlInList(normalizedColumns)})",
            "PostgreSql" => $@"
select a.attname as COLUMN_NAME,
       pgd.description as COMMENTS
  from pg_catalog.pg_statio_all_tables st
  join pg_catalog.pg_description pgd on pgd.objoid = st.relid
  join pg_catalog.pg_attribute a on pgd.objsubid = a.attnum and a.attrelid = st.relid
 where st.schemaname = '{escapedSchema}'
   and pgd.description is not null
   and a.attname in ({BuildSqlInList(normalizedColumns)})",
            "KingbaseES" => $@"
select a.attname as COLUMN_NAME,
       pgd.description as COMMENTS
  from pg_catalog.pg_statio_all_tables st
  join pg_catalog.pg_description pgd on pgd.objoid = st.relid
  join pg_catalog.pg_attribute a on pgd.objsubid = a.attnum and a.attrelid = st.relid
 where st.schemaname = '{escapedSchema}'
   and pgd.description is not null
   and a.attname in ({BuildSqlInList(normalizedColumns)})",
            "SqlServer" => $@"
select c.name as COLUMN_NAME,
       cast(ep.value as nvarchar(4000)) as COMMENTS
  from sys.columns c
  join sys.tables t on c.object_id = t.object_id
  join sys.schemas s on t.schema_id = s.schema_id
    left join sys.extended_properties ep
    on ep.major_id = c.object_id
   and ep.minor_id = c.column_id
   and ep.name = 'MS_Description'
 where s.name = '{escapedSchema}'
   and ep.value is not null
   and c.name in ({BuildSqlInList(normalizedColumns)})",
            _ => string.Empty
        };
    }
    private static string BuildExactResultColumnCommentsSql(
        DatabaseProviderDefinition provider,
        string schema,
        IReadOnlyList<string> tableNames,
        IReadOnlyList<string> columnNames)
    {
        string escapedSchema = EscapeSqlLiteral(schema);
        string[] normalizedTables = tableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] normalizedColumns = columnNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedTables.Length == 0 || normalizedColumns.Length == 0)
        {
            return string.Empty;
        }

        return provider.Name switch
        {
            "Oracle" => $@"select TABLE_NAME, COLUMN_NAME, COMMENTS from ALL_COL_COMMENTS where OWNER = '{escapedSchema.ToUpperInvariant()}' and COMMENTS is not null and TABLE_NAME in ({BuildSqlInList(normalizedTables.Select(item => item.ToUpperInvariant()))}) and COLUMN_NAME in ({BuildSqlInList(normalizedColumns.Select(item => item.ToUpperInvariant()))})",
            "Dameng" => $@"select TABLE_NAME, COLUMN_NAME, COMMENTS from ALL_COL_COMMENTS where OWNER = '{escapedSchema.ToUpperInvariant()}' and COMMENTS is not null and TABLE_NAME in ({BuildSqlInList(normalizedTables.Select(item => item.ToUpperInvariant()))}) and COLUMN_NAME in ({BuildSqlInList(normalizedColumns.Select(item => item.ToUpperInvariant()))})",
            "MySql" => $@"select TABLE_NAME, COLUMN_NAME, COLUMN_COMMENT as COMMENTS from INFORMATION_SCHEMA.COLUMNS where TABLE_SCHEMA = '{escapedSchema}' and COLUMN_COMMENT <> '' and TABLE_NAME in ({BuildSqlInList(normalizedTables)}) and COLUMN_NAME in ({BuildSqlInList(normalizedColumns)})",
            "PostgreSql" => $@"
select st.relname as TABLE_NAME,
       a.attname as COLUMN_NAME,
       pgd.description as COMMENTS
  from pg_catalog.pg_statio_all_tables st
  join pg_catalog.pg_description pgd on pgd.objoid = st.relid
  join pg_catalog.pg_attribute a on pgd.objsubid = a.attnum and a.attrelid = st.relid
 where st.schemaname = '{escapedSchema}'
   and pgd.description is not null
   and st.relname in ({BuildSqlInList(normalizedTables)})
   and a.attname in ({BuildSqlInList(normalizedColumns)})",
            "KingbaseES" => $@"
select st.relname as TABLE_NAME,
       a.attname as COLUMN_NAME,
       pgd.description as COMMENTS
  from pg_catalog.pg_statio_all_tables st
  join pg_catalog.pg_description pgd on pgd.objoid = st.relid
  join pg_catalog.pg_attribute a on pgd.objsubid = a.attnum and a.attrelid = st.relid
 where st.schemaname = '{escapedSchema}'
   and pgd.description is not null
   and st.relname in ({BuildSqlInList(normalizedTables)})
   and a.attname in ({BuildSqlInList(normalizedColumns)})",
            "SqlServer" => $@"
select t.name as TABLE_NAME,
       c.name as COLUMN_NAME,
       cast(ep.value as nvarchar(4000)) as COMMENTS
  from sys.columns c
  join sys.tables t on c.object_id = t.object_id
  join sys.schemas s on t.schema_id = s.schema_id
    left join sys.extended_properties ep
    on ep.major_id = c.object_id
   and ep.minor_id = c.column_id
   and ep.name = 'MS_Description'
 where s.name = '{escapedSchema}'
   and ep.value is not null
   and t.name in ({BuildSqlInList(normalizedTables)})
   and c.name in ({BuildSqlInList(normalizedColumns)})",
            _ => string.Empty
        };
    }

    private static string BuildSqlInList(IEnumerable<string> values)
    {
        return string.Join(", ", values.Select(value => $"'{EscapeSqlLiteral(value)}'"));
    }
    private void ClearMetadataCaches()
    {
        _schemaCache.Clear();
        _tableCache.Clear();
        _viewCache.Clear();
        _materializedViewCache.Clear();
        _functionCache.Clear();
        _procedureCache.Clear();
        _sequenceCache.Clear();
        _triggerCache.Clear();
        _synonymCache.Clear();
        _packageCache.Clear();
        _columnCache.Clear();
        _columnCommentCache.Clear();
        _completionMetadataService.ClearAll();
    }
    private static async Task<IReadOnlyList<string>> ReadSingleColumnAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<string>();
        }

        List<string> items = [];
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string value = reader.GetValue(0)?.ToString() ?? string.Empty;
            if (value.Length > 0)
            {
                items.Add(value);
            }
        }

        return items;
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
    private static string BuildScopeKey(ConnectionProfile profile, string suffix)
    {
        return $"{profile.ProviderName}|{profile.Server}|{profile.Database}|{profile.UserName}|{suffix}";
    }
    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }
}

