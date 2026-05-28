namespace SqlAnalyzer.Core.Models;

public sealed class QueryExecutionRequest
{
    public ConnectionProfile? Connection { get; set; }
    public string Sql { get; set; } = string.Empty;
    public int SqlBaseOffset { get; set; }
    public string Schema { get; set; } = string.Empty;
    public bool IncludeExecutionPlan { get; set; }
    public int MaxPreviewRows { get; set; } = 500;
}
