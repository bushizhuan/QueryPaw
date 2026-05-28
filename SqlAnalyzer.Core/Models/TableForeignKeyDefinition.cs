namespace SqlAnalyzer.Core.Models;

public sealed class TableForeignKeyDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Columns { get; set; } = string.Empty;
    public string ReferenceTable { get; set; } = string.Empty;
    public string ReferenceColumns { get; set; } = string.Empty;
    public string DeleteRule { get; set; } = string.Empty;
}
