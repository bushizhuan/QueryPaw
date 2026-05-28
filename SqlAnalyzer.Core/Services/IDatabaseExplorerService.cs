using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface IDatabaseExplorerService
{
    Task<IReadOnlyList<ObjectNode>> LoadRootNodesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ObjectNode>> LoadChildNodesAsync(ConnectionProfile profile, ObjectNode node, CancellationToken cancellationToken = default);
    IReadOnlyList<CompletionEntry> GetCachedCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null);
    IReadOnlyList<CompletionEntry> GetCachedRelationCompletionSnapshot(ConnectionProfile profile, string? preferredSchema = null);
    IReadOnlyList<CompletionEntry> GetCachedObjectColumnCompletionSnapshot(ConnectionProfile profile, string objectName, string? preferredSchema = null);
    Task<IReadOnlyList<CompletionEntry>> LoadCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionEntry>> LoadRelationCompletionSnapshotAsync(ConnectionProfile profile, string? preferredSchema = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionEntry>> LoadObjectColumnCompletionSnapshotAsync(ConnectionProfile profile, string objectName, string? preferredSchema = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionEntry>> SearchRelationCompletionEntriesAsync(ConnectionProfile profile, string prefix, string? preferredSchema = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> LoadCompletionItemsAsync(ConnectionProfile profile, string prefix, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> LoadSchemasAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> LoadColumnCommentLookupAsync(
        ConnectionProfile profile,
        string schemaName,
        IReadOnlyList<string> columnNames,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> LoadColumnDisplayNameLookupAsync(
        ConnectionProfile profile,
        string schemaName,
        IReadOnlyList<string> columnNames,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, ResultColumnMetadataEntry>> LoadResultColumnMetadataLookupAsync(
        ConnectionProfile profile,
        string schemaName,
        IReadOnlyList<ResultColumnMetadataRequest> requests,
        CancellationToken cancellationToken = default);
    Task<TableDesignModel> LoadTableDesignAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default);
    Task<string> ExportTableStructureAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default);
    Task<string> ExportTableDataAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default);
    Task SaveTableDesignAsync(ConnectionProfile profile, TableDesignModel originalDesign, TableDesignModel updatedDesign, CancellationToken cancellationToken = default);
    Task<string> ValidateConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
}
