namespace SqlAnalyzer.App.Models;

public sealed class SearchMatchResult
{
    public bool Found { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
}
