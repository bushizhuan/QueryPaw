namespace SqlAnalyzer.App.Models;

public sealed class ExplorerActionResult
{
    public string SuggestedFileName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string ErrorTitle { get; init; } = string.Empty;
}
