using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Converters;

public sealed class ResultRowValueConverter : IValueConverter
{
    private readonly int _columnIndex;
    public ResultRowValueConverter(int columnIndex)
    {
        _columnIndex = Math.Max(columnIndex, 0);
    }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ResultRowViewItem row)
        {
            return string.Empty;
        }

        return _columnIndex < row.Values.Count
            ? row.Values[_columnIndex]
            : string.Empty;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
