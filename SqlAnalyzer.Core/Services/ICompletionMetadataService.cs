using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface ICompletionMetadataService
{
    IReadOnlyList<CompletionEntry> GetCachedCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null);
    IReadOnlyList<CompletionEntry> GetCachedRelationCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null);
    IReadOnlyList<CompletionEntry> GetCachedObjectColumnCompletionSnapshot(ConnectionProfile profile, string objectName, string? preferredSchema = null);
    Task<IReadOnlyList<CompletionEntry>> LoadCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionEntry>> LoadRelationCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionEntry>> LoadObjectColumnCompletionSnapshotAsync(ConnectionProfile profile, string objectName, string? preferredSchema = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionEntry>> SearchRelationCompletionEntriesAsync(
        ConnectionProfile profile,
        string prefix,
        string? preferredSchema = null,
        CancellationToken cancellationToken = default);
    void Clear(ConnectionProfile profile);
    void ClearAll();
}
