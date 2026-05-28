namespace SqlAnalyzer.Core.Models;

public sealed class ResultColumnMetadataRequest
{
    public string Key { get; set; } = string.Empty;
    public string RawName { get; set; } = string.Empty;
    public string? SourceSchema { get; set; }
    public string? SourceTable { get; set; }
    public string? SourceColumn { get; set; }
}
