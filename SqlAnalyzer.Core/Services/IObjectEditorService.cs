using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface IObjectEditorService
{
    Task<ObjectEditorModel> LoadObjectEditorModelAsync(
        ConnectionProfile profile,
        string schemaName,
        string objectName,
        string objectType,
        CancellationToken cancellationToken = default);
    Task<string> BuildPreviewSqlAsync(
        ConnectionProfile profile,
        ObjectEditorSaveRequest request,
        CancellationToken cancellationToken = default);
    Task<ObjectEditorSaveResult> SaveObjectAsync(
        ConnectionProfile profile,
        ObjectEditorSaveRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ObjectCompileMessage>> ValidateObjectAsync(
        ConnectionProfile profile,
        ObjectEditorSaveRequest request,
        CancellationToken cancellationToken = default);
}
