namespace SqlAnalyzer.Core.Models;

public sealed class LocalizedObjectLabel
{
    public string ProviderName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string LocaleCode { get; set; } = "zh-CN";
    public string DisplayName { get; set; } = string.Empty;
    public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();
    public string Pinyin { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = "comment";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
