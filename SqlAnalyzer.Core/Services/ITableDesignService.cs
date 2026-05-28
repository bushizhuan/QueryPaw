using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface ITableDesignService
{
    Task<TableDesignModel> LoadTableDesignAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default);
    Task<string> ExportTableStructureAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default);
    Task<string> ExportTableDataAsync(ConnectionProfile profile, string schemaName, string tableName, CancellationToken cancellationToken = default);
    Task SaveTableDesignAsync(ConnectionProfile profile, TableDesignModel originalDesign, TableDesignModel updatedDesign, CancellationToken cancellationToken = default);
}
