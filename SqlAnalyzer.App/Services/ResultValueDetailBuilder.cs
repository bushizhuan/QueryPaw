using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Services;

public static class ResultValueDetailBuilder
{
    public static ResultValueDetailState? Build(ResultSetViewItem? resultSet, ResultCellContext? context, int selectedCellCount, UiTextSet uiText)
    {
        if (resultSet == null ||
            context == null ||
            context.ColumnIndex < 0 ||
            context.ColumnIndex >= resultSet.Columns.Count ||
            context.ColumnIndex >= context.Row.Values.Count)
        {
            return null;
        }

        ResultColumnViewItem column = resultSet.Columns[context.ColumnIndex];
        IReadOnlyList<ResultRowViewItem> visibleRows = resultSet.GetViewRows();
        int rowIndex = -1;
        for (int index = 0; index < visibleRows.Count; index++)
        {
            if (ReferenceEquals(visibleRows[index], context.Row))
            {
                rowIndex = index;
                break;
            }
        }

        string displayValue = context.Row.Values[context.ColumnIndex] ?? string.Empty;
        object? rawValue = context.ColumnIndex < context.Row.OriginalValues.Count ? context.Row.OriginalValues[context.ColumnIndex] : null;
        bool isNull = rawValue == null ||
                      rawValue == DBNull.Value ||
                      string.Equals(displayValue, "(null)", StringComparison.OrdinalIgnoreCase);

        return new ResultValueDetailState
        {
            ColumnName = string.IsNullOrWhiteSpace(column.RawName) ? column.HeaderText : column.RawName,
            ColumnComment = column.CommentText ?? string.Empty,
            DataType = ResolveDataType(column),
            SourceName = ResolveSource(column),
            RowNumberText = rowIndex >= 0 ? (rowIndex + 1).ToString(CultureInfo.CurrentCulture) : "--",
            SelectionText = selectedCellCount > 1
                ? string.Format(CultureInfo.CurrentCulture, uiText.ValueDetailSelectionFormat, selectedCellCount)
                : string.Empty,
            ValueText = isNull ? uiText.ValueDetailNullValue : FormatValue(rawValue, displayValue),
            IsNull = isNull
        };
    }

    private static string ResolveDataType(ResultColumnViewItem column)
    {
        string dataTypeName = column.DataTypeName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dataTypeName) && !IsNumericDataTypeName(dataTypeName))
        {
            return dataTypeName;
        }

        return FormatClrTypeName(column.ClrTypeName);
    }

    private static bool IsNumericDataTypeName(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static string FormatClrTypeName(string? clrTypeName)
    {
        string typeName = clrTypeName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        return typeName switch
        {
            "System.String" => "String",
            "System.Char" => "Char",
            "System.Boolean" => "Boolean",
            "System.Byte" => "Byte",
            "System.SByte" => "SByte",
            "System.Int16" => "Int16",
            "System.Int32" => "Int32",
            "System.Int64" => "Int64",
            "System.UInt16" => "UInt16",
            "System.UInt32" => "UInt32",
            "System.UInt64" => "UInt64",
            "System.Single" => "Single",
            "System.Double" => "Double",
            "System.Decimal" => "Decimal",
            "System.DateTime" => "DateTime",
            "System.DateTimeOffset" => "DateTimeOffset",
            "System.TimeSpan" => "TimeSpan",
            "System.Guid" => "Guid",
            "System.Byte[]" => "Binary",
            _ when typeName.StartsWith("System.", StringComparison.Ordinal) => typeName["System.".Length..],
            _ => typeName
        };
    }

    private static string ResolveSource(ResultColumnViewItem column)
    {
        string[] parts =
        [
            column.SourceSchema?.Trim() ?? string.Empty,
            column.SourceTable?.Trim() ?? string.Empty,
            column.SourceColumn?.Trim() ?? string.Empty
        ];
        return string.Join(".", parts.Where(static item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string FormatValue(object? rawValue, string displayValue)
    {
        if (rawValue is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        return rawValue?.ToString() ?? displayValue ?? string.Empty;
    }
}
