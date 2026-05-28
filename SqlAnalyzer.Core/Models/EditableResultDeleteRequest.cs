namespace SqlAnalyzer.Core.Models;

public sealed class EditableResultDeleteRequest
{
    public ConnectionProfile? Connection { get; set; }

    public string Schema { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public IReadOnlyList<EditableResultMutationColumn> Columns { get; set; } = Array.Empty<EditableResultMutationColumn>();

    public IReadOnlyList<EditableResultRowMutation> Rows { get; set; } = Array.Empty<EditableResultRowMutation>();
}
