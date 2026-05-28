using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAnalyzer.App.Models;

public sealed class ResultRowViewItem
{
    public List<string> Values { get; } = [];

    public List<string> OriginalDisplayValues { get; } = [];

    public List<object?> OriginalValues { get; } = [];

    public bool HasChanges =>
        Values.Count == OriginalDisplayValues.Count &&
        Values.Where((value, index) => !string.Equals(value ?? string.Empty, OriginalDisplayValues[index] ?? string.Empty, StringComparison.Ordinal))
            .Any();

    public string this[int index] =>
        index >= 0 && index < Values.Count
            ? Values[index]
            : string.Empty;
    public string ToClipboardText(string separator = "\t", bool blankNulls = false)
    {
        return string.Join(separator, Values.Select(value =>
            blankNulls && string.Equals(value, "(null)", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : value ?? string.Empty));
    }
    public void ResetChanges()
    {
        Values.Clear();
        Values.AddRange(OriginalDisplayValues);
    }
    public void AcceptChanges(IReadOnlyList<object?> persistedValues)
    {
        OriginalValues.Clear();
        OriginalValues.AddRange(persistedValues);
        OriginalDisplayValues.Clear();
        OriginalDisplayValues.AddRange(Values);
    }
}
