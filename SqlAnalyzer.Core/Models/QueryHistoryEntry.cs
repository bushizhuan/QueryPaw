namespace SqlAnalyzer.Core.Models;

public sealed class QueryHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ExecutedAtDisplay => ExecutedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string ConnectionProfileId { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public string SqlPreview => BuildSqlPreview(Sql);
    public bool IncludePlan { get; set; }
    public bool IsSuccess { get; set; }
    public string StatusText => IsSuccess ? "成功" : "失败";
    public int RowCount { get; set; }
    public string DurationText { get; set; } = "--";
    public string Summary { get; set; } = string.Empty;

    private static string BuildSqlPreview(string sql)
    {
        string normalized = string.Join(" ", (sql ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..160] + "...";
    }
}
