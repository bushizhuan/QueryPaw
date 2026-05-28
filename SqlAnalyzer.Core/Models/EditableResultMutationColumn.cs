namespace SqlAnalyzer.Core.Models;

public sealed class EditableResultMutationColumn
{
    public int Index { get; set; }

    public string HeaderName { get; set; } = string.Empty;

    public string? SourceSchema { get; set; }

    public string? SourceTable { get; set; }

    public string? SourceColumn { get; set; }

    public string? DataTypeName { get; set; }

    public string? ClrTypeName { get; set; }

    public bool IsPrimaryKey { get; set; }

    public bool IsEditable { get; set; }
}
