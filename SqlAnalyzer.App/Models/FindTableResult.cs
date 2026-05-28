using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;

public sealed class FindTableResult
{
    public bool Success { get; init; }
    public string Title { get; init; } = "定位表";
    public string Message { get; init; } = string.Empty;
    public ObjectNode? Match { get; init; }
}
