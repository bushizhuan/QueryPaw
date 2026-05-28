using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Services;

public static class ExecutionPlanViewItemBuilder
{
    public static ExecutionPlanViewItem? Build(IReadOnlyList<ResultSetViewItem> resultSets, string providerName, UiTextSet uiText)
    {
        if (resultSets.Count == 0)
        {
            return null;
        }

        StringBuilder formattedText = new();
        HashSet<string> findings = new(StringComparer.OrdinalIgnoreCase);
        int totalRows = 0;
        foreach (ResultSetViewItem resultSet in resultSets)
        {
            if (resultSet.IsMessageOnly)
            {
                continue;
            }

            string resultSetText = FormatResultSet(resultSet, uiText);
            if (string.IsNullOrWhiteSpace(resultSetText))
            {
                continue;
            }

            if (formattedText.Length > 0)
            {
                formattedText.AppendLine();
                formattedText.AppendLine(new string('=', 72));
            }

            string title = string.IsNullOrWhiteSpace(resultSet.Name) ? uiText.Plan : resultSet.Name;
            formattedText.AppendLine(title);
            formattedText.AppendLine(new string('-', Math.Min(Math.Max(title.Length, 12), 72)));
            formattedText.AppendLine(resultSetText);
            totalRows += resultSet.Rows.Count;
            CollectFindings(resultSet, findings, uiText);
        }

        if (formattedText.Length == 0)
        {
            return null;
        }

        ExecutionPlanViewItem viewItem = new()
        {
            ProviderName = providerName,
            Summary = BuildSummary(providerName, resultSets.Count, totalRows, findings.Count, uiText),
            FormattedText = formattedText.ToString().TrimEnd()
        };
        viewItem.Findings.AddRange(findings);
        return viewItem;
    }

    private static string BuildSummary(string providerName, int resultSetCount, int totalRows, int findingsCount, UiTextSet uiText)
    {
        string providerText = string.IsNullOrWhiteSpace(providerName) ? "SQL" : providerName;
        return findingsCount <= 0
            ? string.Format(CultureInfo.CurrentCulture, uiText.PlanSummaryFormat, providerText, resultSetCount, totalRows)
            : string.Format(CultureInfo.CurrentCulture, uiText.PlanSummaryWithFindingsFormat, providerText, resultSetCount, totalRows, findingsCount);
    }

    private static string FormatResultSet(ResultSetViewItem resultSet, UiTextSet uiText)
    {
        if (resultSet.Columns.Count == 0)
        {
            return uiText.PlanEmptyText;
        }

        if (resultSet.Columns.Count == 1)
        {
            return string.Join(Environment.NewLine, resultSet.Rows.Select(row => row.Values.Count > 0 ? row.Values[0] : string.Empty));
        }

        int count = resultSet.Columns.Count;
        string[] headers = resultSet.Columns
            .Select(column => string.IsNullOrWhiteSpace(column.RawName) ? column.HeaderText : column.RawName)
            .ToArray();
        int[] widths = new int[count];
        for (int columnIndex = 0; columnIndex < count; columnIndex++)
        {
            int headerLength = headers[columnIndex].Length;
            int valueLength = resultSet.Rows
                .Select(row => columnIndex < row.Values.Count ? row.Values[columnIndex].Length : 0)
                .DefaultIfEmpty(0)
                .Max();
            widths[columnIndex] = Math.Min(Math.Max(headerLength, valueLength), 28);
        }

        StringBuilder builder = new();
        builder.AppendLine(string.Join(" | ", headers.Select((header, index) => FormatCell(header, widths[index]))));
        builder.AppendLine(string.Join("-+-", widths.Select(width => new string('-', width))));
        foreach (ResultRowViewItem row in resultSet.Rows)
        {
            builder.AppendLine(string.Join(" | ", Enumerable.Range(0, count)
                .Select(index => FormatCell(index < row.Values.Count ? row.Values[index] : string.Empty, widths[index]))));
        }

        return builder.ToString().TrimEnd();

        static string FormatCell(string value, int width)
        {
            if (value.Length > width)
            {
                return width <= 1 ? value[..1] : value[..(width - 1)] + "…";
            }

            return value.PadRight(width);
        }
    }

    private static void CollectFindings(ResultSetViewItem resultSet, HashSet<string> findings, UiTextSet uiText)
    {
        for (int rowIndex = 0; rowIndex < resultSet.Rows.Count; rowIndex++)
        {
            ResultRowViewItem row = resultSet.Rows[rowIndex];
            for (int columnIndex = 0; columnIndex < resultSet.Columns.Count; columnIndex++)
            {
                string rawName = resultSet.Columns[columnIndex].RawName;
                string value = columnIndex < row.Values.Count ? row.Values[columnIndex] : string.Empty;
                string normalizedValue = value.Trim().ToLowerInvariant();
                string normalizedColumn = rawName.Trim().ToLowerInvariant();

                if (normalizedValue.Contains("full scan") || normalizedValue.Contains("table scan") || normalizedValue.Contains("seq scan"))
                {
                    findings.Add(uiText.PlanFindingFullScan);
                }

                if (normalizedValue.Contains("filesort") ||
                    normalizedValue.Contains(" sort") ||
                    normalizedValue.StartsWith("sort", StringComparison.Ordinal) ||
                    normalizedValue.Contains("using filesort"))
                {
                    findings.Add(uiText.PlanFindingSort);
                }

                if (normalizedValue.Contains("temporary") ||
                    normalizedValue.Contains(" temp") ||
                    normalizedValue.StartsWith("temp", StringComparison.Ordinal) ||
                    normalizedValue.Contains("using temporary"))
                {
                    findings.Add(uiText.PlanFindingTemporary);
                }

                if ((normalizedColumn.Contains("rows") || normalizedColumn.Contains("cardinality")) &&
                    TryExtractNumericValue(value, out double rowCount) &&
                    rowCount >= 10000.0)
                {
                    findings.Add(string.Format(CultureInfo.CurrentCulture, uiText.PlanFindingHighRowsFormat, rowCount.ToString("0", CultureInfo.InvariantCulture)));
                }

                if (normalizedColumn.Contains("cost") &&
                    TryExtractNumericValue(value, out double cost) &&
                    cost >= 1000.0)
                {
                    findings.Add(string.Format(CultureInfo.CurrentCulture, uiText.PlanFindingHighCostFormat, cost.ToString("0.##", CultureInfo.InvariantCulture)));
                }
            }
        }
    }

    private static bool TryExtractNumericValue(string text, out double value)
    {
        value = 0.0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        StringBuilder builder = new();
        bool started = false;
        foreach (char character in text)
        {
            if (!started)
            {
                if (char.IsDigit(character) || character == '-' || character == '.')
                {
                    builder.Append(character);
                    started = true;
                }

                continue;
            }

            if (char.IsDigit(character) || character == '.' || character == ',')
            {
                if (character != ',')
                {
                    builder.Append(character);
                }

                continue;
            }

            break;
        }

        return started && double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
