using System;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Services;

public sealed class SearchController
{
    public SearchMatchResult FindNext(string text, string searchText, int selectionStart, int selectionLength)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
        {
            return new SearchMatchResult();
        }

        int startIndex = Math.Max(selectionStart + selectionLength, 0);
        int foundIndex = text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
        if (foundIndex < 0 && startIndex > 0)
        {
            foundIndex = text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
        }

        return foundIndex >= 0
            ? new SearchMatchResult { Found = true, Start = foundIndex, Length = searchText.Length }
            : new SearchMatchResult();
    }
    public string ReplaceCurrent(string text, string selectedText, string searchText, string replacement, int selectionStart, int selectionLength, out SearchMatchResult nextMatch)
    {
        nextMatch = new SearchMatchResult();
        if (string.IsNullOrEmpty(searchText))
        {
            return text;
        }

        string updated = text;
        if (string.Equals(selectedText, searchText, StringComparison.OrdinalIgnoreCase))
        {
            updated = ReplaceRange(text, selectionStart, selectionLength, replacement);
            int nextStartSeed = Math.Clamp(selectionStart + replacement.Length, 0, updated.Length);
            nextMatch = FindNext(updated, searchText, nextStartSeed, 0);
            return updated;
        }

        nextMatch = FindNext(text, searchText, selectionStart, selectionLength);
        return updated;
    }
    public string ReplaceAll(string text, string searchText, string replacement)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return text;
        }

        return (text ?? string.Empty).Replace(searchText, replacement ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
    public string ReplaceRange(string text, int selectionStart, int selectionLength, string replacement)
    {
        string source = text ?? string.Empty;
        int safeStart = Math.Clamp(selectionStart, 0, source.Length);
        int safeLength = Math.Min(Math.Max(selectionLength, 0), source.Length - safeStart);
        return source.Remove(safeStart, safeLength).Insert(safeStart, replacement ?? string.Empty);
    }
}
