using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface ISqlExecutionService
{
    Task<QueryExecutionResult> ExecuteAsync(QueryExecutionRequest request, CancellationToken cancellationToken = default);
    Task<EditableResultMutationResult> SaveEditableResultAsync(EditableResultSaveRequest request, CancellationToken cancellationToken = default);
    Task<EditableResultMutationResult> DeleteEditableResultRowsAsync(EditableResultDeleteRequest request, CancellationToken cancellationToken = default);
}
