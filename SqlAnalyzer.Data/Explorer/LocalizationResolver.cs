using System.Collections.Concurrent;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.Data.Explorer;

public sealed class LocalizationResolver : ILocalizationResolver
{
    private readonly ILocalizationImportService _commentSource;
    private readonly ConcurrentDictionary<string, LocalizationDictionarySnapshot> _snapshotCache = new(StringComparer.OrdinalIgnoreCase);
    public LocalizationResolver(ILocalizationImportService commentSource)
    {
        _commentSource = commentSource;
    }

    // 本地化词典按连接级缓存，保证对象树、补全、结果列头都能复用同一份快照，避免重复读盘。
    public async Task<LocalizationDictionarySnapshot> GetSnapshotAsync(
        ConnectionProfile profile,
        IReadOnlyList<string>? schemas = null,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = BuildCacheKey(profile, schemas);
        if (_snapshotCache.TryGetValue(cacheKey, out LocalizationDictionarySnapshot? cached))
        {
            return cached;
        }

        LocalizationDictionarySnapshot snapshot = await _commentSource.ImportFromCommentsAsync(profile, schemas, cancellationToken);
        _snapshotCache[cacheKey] = snapshot;
        return snapshot;
    }
    public void Invalidate(ConnectionProfile profile)
    {
        string prefix = BuildConnectionCachePrefix(profile);
        foreach (string key in _snapshotCache.Keys.Where(staticKey => staticKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _snapshotCache.TryRemove(key, out _);
        }
    }
    public string ResolveObjectDisplayName(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string objectType,
        string fallbackDisplayName)
    {
        LocalizedObjectLabel? label = snapshot.ObjectLabels.FirstOrDefault(item =>
            item.IsEnabled &&
            string.Equals(item.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ObjectName, objectName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ObjectType, objectType, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(label?.DisplayName) ? fallbackDisplayName : label.DisplayName.Trim();
    }

    // 列显示名必须按 schema + object + column 精确命中，只有这样才能避免同名字段跨表串标。
    public string ResolveColumnDisplayName(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string columnName,
        string fallbackDisplayName)
    {
        LocalizedColumnLabel? label = snapshot.ColumnLabels.FirstOrDefault(item =>
            item.IsEnabled &&
            string.Equals(item.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ObjectName, objectName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(label?.DisplayName) ? fallbackDisplayName : label.DisplayName.Trim();
    }
    public IReadOnlyList<string> BuildObjectMatchKeys(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string objectType,
        IReadOnlyList<string>? baseKeys = null)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        AddKeys(keys, baseKeys);

        LocalizedObjectLabel? label = snapshot.ObjectLabels.FirstOrDefault(item =>
            item.IsEnabled &&
            string.Equals(item.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ObjectName, objectName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ObjectType, objectType, StringComparison.OrdinalIgnoreCase));
        if (label == null)
        {
            return keys.ToArray();
        }

        AddValue(keys, label.DisplayName);
        return keys.ToArray();
    }

    // 列匹配键会把真实名、显示名、别名、拼音和首字母统一收进来，供补全和定位能力复用。
    public IReadOnlyList<string> BuildColumnMatchKeys(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string columnName,
        IReadOnlyList<string>? baseKeys = null)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        AddKeys(keys, baseKeys);

        LocalizedColumnLabel? label = snapshot.ColumnLabels.FirstOrDefault(item =>
            item.IsEnabled &&
            string.Equals(item.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ObjectName, objectName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
        if (label == null)
        {
            return keys.ToArray();
        }

        AddValue(keys, label.DisplayName);
        return keys.ToArray();
    }
    private static string BuildCacheKey(ConnectionProfile profile, IReadOnlyList<string>? schemas)
    {
        string schemaKey = BuildSchemaCacheSegment(schemas);
        return $"{BuildConnectionCachePrefix(profile)}|{schemaKey}";
    }
    private static string BuildConnectionCachePrefix(ConnectionProfile profile)
    {
        return $"{profile.ProviderName}|{profile.Server}|{profile.Database}|{profile.UserName}";
    }
    private static string BuildSchemaCacheSegment(IReadOnlyList<string>? schemas)
    {
        if (schemas == null || schemas.Count == 0)
        {
            return "*default";
        }

        string[] normalizedSchemas = schemas
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalizedSchemas.Length == 0 ? "*default" : string.Join(",", normalizedSchemas);
    }
    private static void AddKeys(HashSet<string> keys, IReadOnlyList<string>? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (string value in values)
        {
            AddValue(keys, value);
        }
    }
    private static void AddValue(HashSet<string> keys, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string normalized = value.Trim();
        keys.Add(normalized);

        string compact = BuildCompactMatchValue(normalized);
        if (!string.IsNullOrWhiteSpace(compact))
        {
            keys.Add(compact);
        }
    }
    private static string BuildCompactMatchValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] buffer = value.Where(static ch => !char.IsWhiteSpace(ch)).ToArray();
        return buffer.Length == value.Length ? value : new string(buffer);
    }
}
