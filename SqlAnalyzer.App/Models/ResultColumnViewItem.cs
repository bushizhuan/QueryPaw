namespace SqlAnalyzer.App.Models;

public sealed class ResultColumnViewItem
{
    public string RawName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string HeaderText { get; set; } = string.Empty;

    public string CommentText { get; set; } = string.Empty;

    public string? SourceSchema { get; set; }

    public string? SourceTable { get; set; }

    public string? SourceColumn { get; set; }

    public string? DataTypeName { get; set; }

    public string? ClrTypeName { get; set; }

    public bool IsPrimaryKey { get; set; }

    public bool IsEditable { get; set; }

    public bool HasExplicitAlias { get; set; }

    public double? ManualWidth { get; set; }

    public string EffectiveSourceColumn =>
        string.IsNullOrWhiteSpace(SourceColumn) ? RawName : SourceColumn!;
}
