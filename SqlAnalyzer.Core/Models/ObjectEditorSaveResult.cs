namespace SqlAnalyzer.Core.Models;

public sealed class ObjectEditorSaveResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ExecutedSql { get; set; } = string.Empty;

    public IReadOnlyList<ObjectCompileMessage> CompileMessages { get; set; } = Array.Empty<ObjectCompileMessage>();
}
