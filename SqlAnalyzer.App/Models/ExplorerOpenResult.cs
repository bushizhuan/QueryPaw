using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;

public sealed class ExplorerOpenResult
{
    public bool Success { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ObjectNode? Node { get; init; }
}
