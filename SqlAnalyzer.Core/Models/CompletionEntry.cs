namespace SqlAnalyzer.Core.Models;

public sealed class CompletionEntry
{
    public string DisplayText { get; set; } = string.Empty;
    public string InsertText { get; set; } = string.Empty;
    public string Kind { get; set; } = "keyword";
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<string> MatchKeys { get; set; } = Array.Empty<string>();
    public string SourceObject { get; set; } = string.Empty;
    public int SortWeight { get; set; }
}
