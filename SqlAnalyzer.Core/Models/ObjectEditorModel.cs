namespace SqlAnalyzer.Core.Models;

public sealed class ObjectEditorModel
{
    public string ProviderName { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ObjectType { get; set; } = string.Empty;

    public string CommentText { get; set; } = string.Empty;

    public string ReturnType { get; set; } = string.Empty;

    public string OriginalDefinition { get; set; } = string.Empty;

    public ObjectEditorCapability Capability { get; set; }

    public IReadOnlyList<ObjectParameterDefinition> Parameters { get; set; } = Array.Empty<ObjectParameterDefinition>();
}
