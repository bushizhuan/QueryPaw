using System.Collections.Generic;
using System.Linq;

namespace SqlAnalyzer.App.Models;

public sealed class ExecutionPlanViewItem
{
    public string ProviderName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FormattedText { get; set; } = string.Empty;
    public List<string> Findings { get; } = [];

    public bool HasContent => !string.IsNullOrWhiteSpace(FormattedText);

    public bool HasFindings => Findings.Count > 0;

    public string FindingsText =>
        Findings.Count == 0
            ? string.Empty
            : string.Join(System.Environment.NewLine, Findings.Select((item, index) => $"{index + 1}. {item}"));
}
