namespace SqlAnalyzer.Core.Models;

public sealed class EditableResultMutationResult
{
    public int AffectedRows { get; set; }

    public string Summary { get; set; } = string.Empty;
}
