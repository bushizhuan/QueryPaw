namespace SqlAnalyzer.Core.Models;

public sealed class ModelRelationEdge
{
    public string ConstraintName { get; set; } = string.Empty;

    public string ParentTable { get; set; } = string.Empty;

    public string ChildTable { get; set; } = string.Empty;

    public IReadOnlyList<string> ParentColumns { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ChildColumns { get; set; } = Array.Empty<string>();

    public string RelationType { get; set; } = "ForeignKey";
}
