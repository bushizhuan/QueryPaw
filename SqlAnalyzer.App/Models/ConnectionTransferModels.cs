using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;
public enum ConnectionImportConflictPolicy
{
    Rename,
    Replace,
    Skip
}
public sealed class ConnectionImportPreviewItem
{
    public ConnectionProfile Profile { get; init; } = new();
    public bool IsSelected { get; set; }
    public bool IsImportable { get; set; } = true;
    public bool HasConflict { get; set; }
    public string ImportAction { get; set; } = "Add";
    public string ResolvedName { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string WarningText { get; set; } = string.Empty;
}
public sealed class ConnectionImportResult
{
    public int AddedCount { get; set; }
    public int RenamedCount { get; set; }
    public int ReplacedCount { get; set; }
    public int SkippedCount { get; set; }

    public int ImportedCount => AddedCount + RenamedCount + ReplacedCount;

    public string BuildSummary()
    {
        return $"已导入 {ImportedCount} 个连接。新增 {AddedCount} 个，重命名 {RenamedCount} 个，覆盖 {ReplacedCount} 个，跳过 {SkippedCount} 个。";
    }
}
