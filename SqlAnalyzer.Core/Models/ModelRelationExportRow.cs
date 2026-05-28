namespace SqlAnalyzer.Core.Models;

public sealed class ModelRelationExportRow
{
    public string ParentTable { get; set; } = string.Empty;

    public string ParentComment { get; set; } = string.Empty;

    public string ChildTable { get; set; } = string.Empty;

    public string ChildComment { get; set; } = string.Empty;

    public string ParentColumnsText { get; set; } = string.Empty;

    public string ChildColumnsText { get; set; } = string.Empty;

    public string ConstraintName { get; set; } = string.Empty;
}
