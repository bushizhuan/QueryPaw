namespace SqlAnalyzer.Core.Models;

public sealed class QueryExecutionResult
{
    public IReadOnlyList<QueryResultSet> ResultSets { get; set; } = Array.Empty<QueryResultSet>();
    public string Summary { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public bool IsPreviewTruncated { get; set; }
    public QueryExecutionErrorInfo? Error { get; set; }
}
