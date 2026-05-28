namespace SqlAnalyzer.App.Models;

public sealed class ResultValueDetailState
{
    public string ColumnName { get; init; } = string.Empty;

    public string ColumnComment { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public string RowNumberText { get; init; } = string.Empty;

    public string SelectionText { get; init; } = string.Empty;

    public string ValueText { get; init; } = string.Empty;

    public bool IsNull { get; init; }

    public bool HasColumnComment => !string.IsNullOrWhiteSpace(ColumnComment);

    public bool HasDataType => !string.IsNullOrWhiteSpace(DataType);

    public bool HasSourceName => !string.IsNullOrWhiteSpace(SourceName);

    public bool HasSelectionText => !string.IsNullOrWhiteSpace(SelectionText);
}
