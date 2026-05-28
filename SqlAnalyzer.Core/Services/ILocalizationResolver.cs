using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface ILocalizationResolver
{
    Task<LocalizationDictionarySnapshot> GetSnapshotAsync(ConnectionProfile profile, IReadOnlyList<string>? schemas = null, CancellationToken cancellationToken = default);
    void Invalidate(ConnectionProfile profile);
    string ResolveObjectDisplayName(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string objectType,
        string fallbackDisplayName);
    string ResolveColumnDisplayName(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string columnName,
        string fallbackDisplayName);
    IReadOnlyList<string> BuildObjectMatchKeys(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string objectType,
        IReadOnlyList<string>? baseKeys = null);
    IReadOnlyList<string> BuildColumnMatchKeys(
        LocalizationDictionarySnapshot snapshot,
        string schemaName,
        string objectName,
        string columnName,
        IReadOnlyList<string>? baseKeys = null);
}
