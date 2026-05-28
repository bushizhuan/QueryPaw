using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface ILocalizationImportService
{
    Task<LocalizationDictionarySnapshot> ImportFromCommentsAsync(
        ConnectionProfile profile,
        IReadOnlyList<string>? schemas = null,
        CancellationToken cancellationToken = default);
}
