namespace SqlAnalyzer.Core.Models;

public sealed class EditorSessionState
{
    public int SelectedIndex { get; set; }
    public IReadOnlyList<EditorDocument> Documents { get; set; } = Array.Empty<EditorDocument>();
    public IReadOnlyList<RecentFileEntry> RecentFiles { get; set; } = Array.Empty<RecentFileEntry>();
    public IReadOnlyList<QueryHistoryEntry> QueryHistory { get; set; } = Array.Empty<QueryHistoryEntry>();
    public IReadOnlyDictionary<string, int> RecentCompletionUsage { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> RecentSchemasByConnectionId { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string ResultHeaderMode { get; set; } = "Dual";
}
