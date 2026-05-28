using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface ICommentMaintenanceService
{
    Task<CommentMaintenanceWorkspace> LoadWorkspaceAsync(
        ConnectionProfile profile,
        string schemaName,
        CancellationToken cancellationToken = default);
    Task<CommentImportResult> ImportCsvAsync(
        CommentMaintenanceWorkspace workspace,
        string filePath,
        CancellationToken cancellationToken = default);
    Task ExportCsvAsync(
        CommentMaintenanceWorkspace workspace,
        string filePath,
        CancellationToken cancellationToken = default);
    IReadOnlyList<CommentSqlPreviewItem> BuildSqlPreview(CommentMaintenanceWorkspace workspace);
    Task<int> ApplyChangesAsync(
        ConnectionProfile profile,
        IReadOnlyList<CommentSqlPreviewItem> sqlItems,
        CancellationToken cancellationToken = default);
}
