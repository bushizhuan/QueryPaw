namespace SqlAnalyzer.App.Models;

public sealed class ResultWorkspaceTabItem
{
    public string Header { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public ResultSetViewItem? ResultSet { get; set; }

    public bool IsResultTab => string.Equals(Kind, "result", System.StringComparison.OrdinalIgnoreCase);
    public bool IsMessageTab => string.Equals(Kind, "message", System.StringComparison.OrdinalIgnoreCase);
    public bool IsPlanTab => string.Equals(Kind, "plan", System.StringComparison.OrdinalIgnoreCase);
}
