using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default);
    Task ExportAsync(string filePath, IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectionProfile>> ImportAsync(string filePath, CancellationToken cancellationToken = default);
}
