namespace SqlAnalyzer.App.Models;

public sealed class RecentFileMenuItemModel
{
    public string Header { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public bool IsEnabled { get; init; }
}
