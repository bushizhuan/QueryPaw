namespace SqlAnalyzer.Core.Models;

public sealed class EditableResultRowMutation
{
    public int RowIndex { get; set; }

    public IReadOnlyList<object?> OriginalValues { get; set; } = Array.Empty<object?>();

    public IReadOnlyList<string> OriginalDisplayValues { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> CurrentValues { get; set; } = Array.Empty<string>();
}
