namespace SqlAnalyzer.Core.Models;

public sealed class LocalizationDictionarySnapshot
{
    public string LocaleCode { get; set; } = "zh-CN";
    public IReadOnlyList<LocalizedObjectLabel> ObjectLabels { get; set; } = Array.Empty<LocalizedObjectLabel>();
    public IReadOnlyList<LocalizedColumnLabel> ColumnLabels { get; set; } = Array.Empty<LocalizedColumnLabel>();
}
