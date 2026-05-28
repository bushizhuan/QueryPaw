namespace SqlAnalyzer.Core.Models;

public sealed class RecentFileEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset LastOpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ConnectionProfileId { get; set; } = string.Empty;
    public string DefaultSchema { get; set; } = string.Empty;
}
