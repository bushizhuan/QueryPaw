using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface IEditorSessionStore
{
    Task<EditorSessionState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(EditorSessionState state, CancellationToken cancellationToken = default);
}
