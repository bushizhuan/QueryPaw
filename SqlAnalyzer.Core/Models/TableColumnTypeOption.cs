namespace SqlAnalyzer.Core.Models;

public sealed class TableColumnTypeOption
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool SupportsLength { get; init; }
    public bool SupportsPrecision { get; init; }
    public bool SupportsScale { get; init; }
    public int? DefaultLength { get; init; }
    public int? DefaultPrecision { get; init; }
    public int? DefaultScale { get; init; }
    public override string ToString() => Name;
}
