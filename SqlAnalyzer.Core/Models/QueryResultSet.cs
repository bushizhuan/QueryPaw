namespace SqlAnalyzer.Core.Models;

public sealed class QueryResultSet
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string?> SourceSchemas { get; set; } = Array.Empty<string?>();
    public IReadOnlyList<string?> SourceTables { get; set; } = Array.Empty<string?>();
    public IReadOnlyList<string?> SourceColumns { get; set; } = Array.Empty<string?>();
    public IReadOnlyList<string?> DataTypeNames { get; set; } = Array.Empty<string?>();
    public IReadOnlyList<string?> ClrTypeNames { get; set; } = Array.Empty<string?>();
    public string? BaseSchemaName { get; set; }
    public string? BaseTableName { get; set; }
    public IReadOnlyList<string> PrimaryKeyColumns { get; set; } = Array.Empty<string>();
    public bool CanEdit { get; set; }
    public string EditDisabledReason { get; set; } = string.Empty;
    public IReadOnlyList<object?[]> Rows { get; set; } = Array.Empty<object?[]>();
    public bool IsPreviewTruncated { get; set; }
    public int PreviewLimit { get; set; }
}
