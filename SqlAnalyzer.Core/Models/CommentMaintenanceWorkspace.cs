namespace SqlAnalyzer.Core.Models;

public enum CommentChangeStatus
{
    Unchanged,
    Added,
    Updated,
    Cleared,
    ImportFailed
}

public sealed class CommentMaintenanceWorkspace
{
    public string ProviderName { get; set; } = string.Empty;

    public string ConnectionProfileId { get; set; } = string.Empty;

    public string ConnectionName { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public DateTimeOffset LoadedAt { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<CommentMaintenanceTableEntry> Tables { get; set; } = Array.Empty<CommentMaintenanceTableEntry>();

    public IReadOnlyList<CommentMaintenanceColumnEntry> Columns { get; set; } = Array.Empty<CommentMaintenanceColumnEntry>();
}

public sealed class CommentMaintenanceTableEntry
{
    public string SchemaName { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    public string ObjectType { get; set; } = string.Empty;

    public string CurrentComment { get; set; } = string.Empty;

    public string EditedComment { get; set; } = string.Empty;
}

public sealed class CommentMaintenanceColumnEntry
{
    public string SchemaName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string ObjectType { get; set; } = "table";

    public string ColumnName { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public string FullTypeDefinition { get; set; } = string.Empty;

    public bool IsNullable { get; set; } = true;

    public string DefaultValue { get; set; } = string.Empty;

    public bool IsIdentity { get; set; }

    public bool IsComputed { get; set; }

    public string ExtraDefinition { get; set; } = string.Empty;

    public string CurrentComment { get; set; } = string.Empty;

    public string EditedComment { get; set; } = string.Empty;
}

public sealed class CommentSqlPreviewItem
{
    public string TargetType { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    public string ColumnName { get; set; } = string.Empty;

    public string SqlText { get; set; } = string.Empty;
}

public sealed class CommentImportResult
{
    public int ImportedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public IReadOnlyList<CommentImportErrorItem> Errors { get; set; } = Array.Empty<CommentImportErrorItem>();
}

public sealed class CommentImportErrorItem
{
    public int RowNumber { get; set; }

    public string Message { get; set; } = string.Empty;
}
