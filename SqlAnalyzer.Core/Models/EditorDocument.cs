namespace SqlAnalyzer.Core.Models;

public sealed class EditorDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Query 1";
    public string DocumentKind { get; set; } = "Query";
    public string WorkspaceKey { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ConnectionProfileId { get; set; } = string.Empty;
    public string DefaultSchema { get; set; } = string.Empty;
    public string ObjectSchemaName { get; set; } = string.Empty;
    public string ObjectRawName { get; set; } = string.Empty;
    public string ObjectDisplayName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string ModelSchemaName { get; set; } = string.Empty;
    public string ModelFocusTableName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int CaretOffset { get; set; }
    public bool IsDirty { get; set; }
    public DateTimeOffset? LastFileWriteTimeUtc { get; set; }
}
