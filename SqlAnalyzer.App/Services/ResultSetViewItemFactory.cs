using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class ResultSetViewItemFactory
{
    public static IReadOnlyList<ResultSetViewItem> BuildItems(
        IReadOnlyList<QueryResultSet> resultSets,
        string providerName,
        string resultNavigationLabel,
        UiTextSet uiText,
        Action<string>? diagnosticLog = null)
    {
        List<ResultSetViewItem> items = new(resultSets.Count);
        foreach (QueryResultSet resultSet in resultSets)
        {
            items.Add(Build(resultSet, providerName, uiText, diagnosticLog));
        }

        AssignNavigationTitles(items, resultNavigationLabel);
        return items;
    }

    public static ResultSetViewItem Build(
        QueryResultSet set,
        string providerName,
        UiTextSet uiText,
        Action<string>? diagnosticLog = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ResultSetViewItem item = new()
        {
            ProviderName = providerName,
            Name = set.Name,
            Summary = $"{set.Rows.Count} row(s) / {set.Columns.Count} column(s)",
            IsPreviewTruncated = set.IsPreviewTruncated,
            PreviewLimit = set.PreviewLimit,
            IsMessageOnly = set.Columns.Count == 1 && string.Equals(set.Columns[0], "Message", StringComparison.OrdinalIgnoreCase),
            BaseSchemaName = set.BaseSchemaName,
            BaseTableName = set.BaseTableName,
            CanEdit = set.CanEdit,
            EditDisabledReason = set.EditDisabledReason,
            IsEditMode = false
        };

        item.PrimaryKeyColumns.AddRange(set.PrimaryKeyColumns);
        if (item.IsMessageOnly && set.Rows.Count > 0 && set.Rows[0].Length != 0)
        {
            item.MessageText = set.Rows[0][0]?.ToString() ?? string.Empty;
        }

        IReadOnlyList<ResultColumnViewItem> columns = BuildColumns(set);
        item.Columns.Capacity = columns.Count;
        foreach (ResultColumnViewItem column in columns)
        {
            item.Columns.Add(column);
        }

        item.Rows.Capacity = set.Rows.Count;
        foreach (object?[] row in set.Rows)
        {
            ResultRowViewItem rowItem = new();
            rowItem.Values.Capacity = item.Columns.Count;
            rowItem.OriginalDisplayValues.Capacity = item.Columns.Count;
            rowItem.OriginalValues.Capacity = item.Columns.Count;
            for (int index = 0; index < item.Columns.Count; index++)
            {
                object? rawValue = index < row.Length ? row[index] : null;
                string displayValue = FormatCellValue(rawValue);
                // Keep both the display text and raw value; edit/save needs the raw value, copy/export uses the display text.
                rowItem.Values.Add(displayValue);
                rowItem.OriginalDisplayValues.Add(displayValue);
                rowItem.OriginalValues.Add(rawValue);
            }

            item.Rows.Add(rowItem);
        }

        if (set.IsPreviewTruncated)
        {
            item.Summary = string.Format(CultureInfo.CurrentCulture, uiText.PreviewTruncatedSummaryFormat, set.Rows.Count);
        }

        if (item.PreviewLimit <= 0)
        {
            item.PreviewLimit = set.Rows.Count;
        }

        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds >= 30)
        {
            diagnosticLog?.Invoke($"BuildResultSetViewItem: name={set.Name}; elapsedMs={stopwatch.ElapsedMilliseconds}; columns={set.Columns.Count}; rows={set.Rows.Count}");
        }

        return item;
    }

    public static void AssignNavigationTitles(IEnumerable<ResultSetViewItem> resultSets, string resultNavigationLabel)
    {
        string label = NormalizeNavigationLabel(resultNavigationLabel);
        int index = 1;
        foreach (ResultSetViewItem resultSet in resultSets.Where(IsTabular))
        {
            resultSet.NavigationTitle = $"{label} {index++}";
        }
    }

    public static bool IsTabular(ResultSetViewItem? item)
    {
        return item != null && !item.IsMessageOnly && item.Columns.Count > 0;
    }

    private static string NormalizeNavigationLabel(string resultNavigationLabel)
    {
        if (string.Equals(resultNavigationLabel, "Results", StringComparison.OrdinalIgnoreCase))
        {
            return "Result";
        }

        return string.IsNullOrWhiteSpace(resultNavigationLabel) ? "Result" : resultNavigationLabel.Trim();
    }

    private static IReadOnlyList<ResultColumnViewItem> BuildColumns(QueryResultSet set)
    {
        Dictionary<string, int> columnNameCounts = new(StringComparer.OrdinalIgnoreCase);
        List<ResultColumnViewItem> columns = new();
        HashSet<string> primaryKeyColumns = set.PrimaryKeyColumns
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < set.Columns.Count; index++)
        {
            string sourceName = set.Columns[index];
            string rawName = string.IsNullOrWhiteSpace(sourceName) ? "Column" : sourceName.Trim();
            columnNameCounts.TryGetValue(rawName, out int duplicateCount);
            columnNameCounts[rawName] = duplicateCount + 1;

            string headerText = duplicateCount == 0 ? rawName : $"{rawName} ({duplicateCount + 1})";
            string? sourceTable = index < set.SourceTables.Count ? set.SourceTables[index] : null;
            string? sourceColumn = index < set.SourceColumns.Count && !string.IsNullOrWhiteSpace(set.SourceColumns[index])
                ? set.SourceColumns[index]
                : rawName;
            bool isPrimaryKey = !string.IsNullOrWhiteSpace(sourceColumn) && primaryKeyColumns.Contains(sourceColumn.Trim());
            bool isEditable = set.CanEdit &&
                              !isPrimaryKey &&
                              !string.IsNullOrWhiteSpace(sourceColumn) &&
                              (string.IsNullOrWhiteSpace(sourceTable) ||
                               string.Equals(sourceTable, set.BaseTableName, StringComparison.OrdinalIgnoreCase));

            columns.Add(new ResultColumnViewItem
            {
                RawName = rawName,
                DisplayName = rawName,
                HeaderText = headerText,
                CommentText = string.Empty,
                SourceSchema = index < set.SourceSchemas.Count ? set.SourceSchemas[index] : null,
                SourceTable = sourceTable,
                SourceColumn = sourceColumn,
                DataTypeName = index < set.DataTypeNames.Count ? set.DataTypeNames[index] : null,
                ClrTypeName = index < set.ClrTypeNames.Count ? set.ClrTypeNames[index] : null,
                IsPrimaryKey = isPrimaryKey,
                IsEditable = isEditable
            });
        }

        return columns;
    }

    private static string FormatCellValue(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "(null)";
        }

        if (value is DateTime dateTime)
        {
            return dateTime.TimeOfDay == TimeSpan.Zero
                ? dateTime.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)
                : dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture);
        }

        return value.ToString() ?? string.Empty;
    }
}
