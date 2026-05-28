namespace SqlAnalyzer.App.Models;

public sealed class ObjectDetailsModel
{
    public string WindowTitle { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string RawName { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
    public string SchemaName { get; init; } = string.Empty;
    public string ObjectType { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DdlText { get; init; } = string.Empty;
    public string PreviewSql { get; init; } = string.Empty;
    public bool HasDdl => !string.IsNullOrWhiteSpace(DdlText);
    public bool HasPreviewSql => !string.IsNullOrWhiteSpace(PreviewSql);
}
