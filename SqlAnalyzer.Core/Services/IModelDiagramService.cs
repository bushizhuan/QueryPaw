using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface IModelDiagramService
{
    Task<ModelDiagramWorkspace> LoadSchemaModelAsync(
        ConnectionProfile profile,
        string schemaName,
        CancellationToken cancellationToken = default);

    Task<ModelDiagramWorkspace> LoadTableNeighborhoodAsync(
        ConnectionProfile profile,
        string schemaName,
        string tableName,
        int depth,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelRelationExportRow>> ExportRelationRowsAsync(
        ConnectionProfile profile,
        ModelDiagramWorkspace workspace,
        CancellationToken cancellationToken = default);
}
