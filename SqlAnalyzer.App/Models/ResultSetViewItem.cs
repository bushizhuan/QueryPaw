using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SqlAnalyzer.App.Models;

public sealed class ResultSetViewItem
{
    public string ProviderName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NavigationTitle { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public bool IsPreviewTruncated { get; set; }

    public int PreviewLimit { get; set; }

    public int LoadedRowCount => Rows.Count;

    public bool IsMessageOnly { get; set; }

    public string MessageText { get; set; } = string.Empty;

    public string? BaseSchemaName { get; set; }

    public string? BaseTableName { get; set; }

    public bool CanEdit { get; set; }

    public string EditDisabledReason { get; set; } = string.Empty;

    public bool IsEditMode { get; set; }

    public List<string> PrimaryKeyColumns { get; } = [];

    public List<ResultColumnViewItem> Columns { get; } = [];

    public List<ResultRowViewItem> Rows { get; } = [];

    public string FilterText { get; set; } = string.Empty;

    public int? SortColumnIndex { get; set; }

    public bool SortDescending { get; set; }

    public int? PinnedColumnIndex { get; set; }

    public bool HasPendingChanges => Rows.Any(static row => row.HasChanges);

    public bool CanDeleteRows => CanEdit && IsEditMode;

    public string HeaderClipboardText =>
        string.Join("\t", Columns.Select(column => column.HeaderText));
    public string ToClipboardText(bool includeHeader, Func<ResultColumnViewItem, string>? headerFormatter = null)
    {
        StringBuilder builder = new();
        if (includeHeader)
        {
            builder.AppendLine(BuildHeaderClipboardText(headerFormatter));
        }

        foreach (ResultRowViewItem row in GetViewRows())
        {
            builder.AppendLine(row.ToClipboardText());
        }

        return builder.ToString().TrimEnd();
    }
    public string ToCsv(Func<ResultColumnViewItem, string>? headerFormatter = null)
    {
        StringBuilder builder = new();
        builder.AppendLine(string.Join(",", Columns.Select(column => EscapeCsv(FormatHeader(column, headerFormatter)))));
        foreach (ResultRowViewItem row in GetViewRows())
        {
            builder.AppendLine(string.Join(",", row.Values.Select(EscapeCsv)));
        }

        return builder.ToString();
    }
    public string ToJson(Func<ResultColumnViewItem, string>? headerFormatter = null)
    {
        List<Dictionary<string, string>> rows = [];
        foreach (ResultRowViewItem row in GetViewRows())
        {
            Dictionary<string, string> item = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < Columns.Count; index++)
            {
                string key = FormatHeader(Columns[index], headerFormatter);
                string value = index < row.Values.Count ? row.Values[index] : string.Empty;
                item[key] = value;
            }

            rows.Add(item);
        }

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
    public bool TryBuildInsertScript(IEnumerable<ResultRowViewItem> rows, out string script, out string errorMessage)
    {
        ResultRowViewItem[] targetRows = rows?.ToArray() ?? [];
        if (targetRows.Length == 0)
        {
            script = string.Empty;
            errorMessage = "当前没有选中的结果行。";
            return false;
        }

        string[] columnNames = Columns
            .Select(column => !string.IsNullOrWhiteSpace(column.SourceColumn) ? column.SourceColumn!.Trim() : column.RawName.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (columnNames.Length == 0 || columnNames.Length != Columns.Count)
        {
            script = string.Empty;
            errorMessage = "当前结果集缺少可用的源字段信息，不能生成 INSERT 语句。";
            return false;
        }

        StringBuilder builder = new();
        string columnSegment = string.Join(", ", columnNames);
        foreach (ResultRowViewItem row in targetRows)
        {
            IEnumerable<string> valueLiterals = columnNames.Select((_, index) =>
            {
                string value = index < row.Values.Count ? row.Values[index] : string.Empty;
                return ToInsertLiteral(ProviderName, value);
            });

            builder.Append("insert into ")
                .Append("''")
                .Append(" (")
                .Append(columnSegment)
                .Append(") values (")
                .Append(string.Join(", ", valueLiterals))
                .AppendLine(");");
        }

        script = builder.ToString().TrimEnd();
        errorMessage = string.Empty;
        return true;
    }
    public IReadOnlyList<ResultRowViewItem> GetViewRows()
    {
        IEnumerable<ResultRowViewItem> query = Rows;

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            string keyword = FilterText.Trim();
            query = query.Where(row => row.Values.Any(value =>
                !string.IsNullOrWhiteSpace(value) &&
                value.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        if (SortColumnIndex.HasValue)
        {
            int columnIndex = SortColumnIndex.Value;
            query = SortDescending
                ? query.OrderByDescending(row => row[columnIndex], StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(row => row[columnIndex], StringComparer.OrdinalIgnoreCase);
        }

        return query.ToArray();
    }
    public void ResetPendingChanges()
    {
        foreach (ResultRowViewItem row in Rows)
        {
            row.ResetChanges();
        }
    }

    private string BuildHeaderClipboardText(Func<ResultColumnViewItem, string>? headerFormatter)
    {
        return string.Join("\t", Columns.Select(column => FormatHeader(column, headerFormatter)));
    }

    private static string FormatHeader(ResultColumnViewItem column, Func<ResultColumnViewItem, string>? headerFormatter)
    {
        return headerFormatter?.Invoke(column) ?? column.HeaderText;
    }

    private static string EscapeCsv(string value)
    {
        string normalized = value ?? string.Empty;
        if (normalized.Contains('"'))
        {
            normalized = normalized.Replace("\"", "\"\"");
        }

        if (normalized.IndexOfAny([',', '"', '\r', '\n']) >= 0)
        {
            return $"\"{normalized}\"";
        }

        return normalized;
    }

    private static string ToInsertLiteral(string providerName, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "(null)", StringComparison.OrdinalIgnoreCase))
        {
            return "null";
        }

        if (TryParseDateTime(value, out DateTime dateTime))
        {
            bool isDateOnly = IsDateOnly(value, dateTime);
            return BuildDateLiteral(providerName, dateTime, isDateOnly);
        }

        string escaped = value.Replace("'", "''");
        return $"'{escaped}'";
    }

    private static bool TryParseDateTime(string value, out DateTime dateTime)
    {
        return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out dateTime) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime);
    }

    private static bool IsDateOnly(string originalValue, DateTime dateTime)
    {
        if (dateTime.TimeOfDay != TimeSpan.Zero)
        {
            return false;
        }

        string trimmed = originalValue.Trim();
        return !trimmed.Contains(':');
    }

    private static string BuildDateLiteral(string providerName, DateTime dateTime, bool isDateOnly)
    {
        string normalizedProvider = providerName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (isDateOnly)
        {
            return normalizedProvider switch
            {
                "oracle" => $"to_date('{dateTime:yyyy-MM-dd}', 'YYYY-MM-DD')",
                "postgresql" or "kingbasees" => $"date '{dateTime:yyyy-MM-dd}'",
                "mysql" => $"STR_TO_DATE('{dateTime:yyyy-MM-dd}', '%Y-%m-%d')",
                "sqlserver" => $"cast('{dateTime:yyyy-MM-dd}' as date)",
                _ => $"'{dateTime:yyyy-MM-dd}'"
            };
        }

        return normalizedProvider switch
        {
            "oracle" => $"to_timestamp('{dateTime:yyyy-MM-dd HH:mm:ss.fff}', 'YYYY-MM-DD HH24:MI:SS.FF3')",
            "postgresql" or "kingbasees" => $"timestamp '{dateTime:yyyy-MM-dd HH:mm:ss.fff}'",
            "mysql" => $"STR_TO_DATE('{dateTime:yyyy-MM-dd HH:mm:ss.fff}', '%Y-%m-%d %H:%i:%s.%f')",
            "sqlserver" => $"cast('{dateTime:yyyy-MM-dd HH:mm:ss.fff}' as datetime2)",
            _ => $"'{dateTime:yyyy-MM-dd HH:mm:ss.fff}'"
        };
    }
}
