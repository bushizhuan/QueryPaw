namespace SqlAnalyzer.Core.Models;

public sealed class ObjectEditorSaveRequest
{
    public string SchemaName { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    public string ObjectType { get; set; } = string.Empty;

    public string Definition { get; set; } = string.Empty;
}
