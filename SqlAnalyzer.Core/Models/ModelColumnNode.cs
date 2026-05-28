namespace SqlAnalyzer.Core.Models;

public sealed class ModelColumnNode
{
    public string ColumnName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string CommentText { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public bool IsPrimaryKey { get; set; }

    public bool IsForeignKey { get; set; }
}
