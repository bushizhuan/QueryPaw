namespace SqlAnalyzer.App.Models;

public sealed class ResultCellContext
{
    public required ResultRowViewItem Row { get; init; }

    public int ColumnIndex { get; init; }
}
