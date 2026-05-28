namespace SqlAnalyzer.Infrastructure.Storage;

public sealed class WorkspaceLayoutOptions
{
    public string BaseDirectory { get; init; } = string.Empty;
    public string ConnectionsFileName { get; init; } = "connections.wlb";
    public string SessionFileName { get; init; } = "editor-session.json";
    public string LocalizationFileName { get; init; } = "localization-dictionary.json";
}
