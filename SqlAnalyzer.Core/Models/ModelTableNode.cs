namespace SqlAnalyzer.Core.Models;

public sealed class ModelTableNode
{
    public string SchemaName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string CommentText { get; set; } = string.Empty;

    public IReadOnlyList<ModelColumnNode> Columns { get; set; } = Array.Empty<ModelColumnNode>();

    public IReadOnlyList<string> PrimaryKeyColumns { get; set; } = Array.Empty<string>();

    public int ForeignKeyCount { get; set; }

    public int ReferencedByCount { get; set; }

    public int ReferencesCount { get; set; }
}
