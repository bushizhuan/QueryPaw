namespace SqlAnalyzer.Core.Models;

public sealed class TableIndexDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Columns { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public string IndexType { get; set; } = string.Empty;
}
