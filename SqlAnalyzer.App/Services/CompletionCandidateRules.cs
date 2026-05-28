using System;
using System.Collections.Generic;
using System.Linq;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class CompletionCandidateRules
{
    public static int GetTakeLimit(string completionContext)
    {
        if (string.Equals(completionContext, "member-column", StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (string.Equals(completionContext, "column", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(completionContext, "select-column", StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        return 40;
    }

    public static int GetRank(
        CompletionItem item,
        string normalizedPrefix,
        string selectedSchema,
        string completionContext,
        string? resolvedObjectName,
        string? singleRelationName,
        IReadOnlyDictionary<string, int> recentUsage)
    {
        int rank = item.SortWeight * 1000;
        bool isLocalizedSearch = ContainsLocalizedText(normalizedPrefix);
        bool startsWithPrefix = !string.IsNullOrWhiteSpace(normalizedPrefix) &&
            item.MatchKeys.Any(text => text.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase));
        bool containsPrefix = !startsWithPrefix &&
            normalizedPrefix.Length >= 2 &&
            item.MatchKeys.Any(text => text.Contains(normalizedPrefix, StringComparison.OrdinalIgnoreCase));

        if (startsWithPrefix)
        {
            rank -= 700;
        }
        else if (containsPrefix)
        {
            rank -= 300;
        }

        if (isLocalizedSearch)
        {
            if (startsWithPrefix)
            {
                rank -= 450;
            }
            else if (containsPrefix)
            {
                rank -= 180;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedSchema) &&
            item.Description.StartsWith(selectedSchema + ".", StringComparison.OrdinalIgnoreCase))
        {
            rank -= 450;
        }

        string key = string.IsNullOrWhiteSpace(item.SourceObject) ? item.InsertText : item.SourceObject;
        if (recentUsage.TryGetValue(key, out int usageCount))
        {
            rank -= Math.Min(usageCount, 20) * 35;
        }

        if (string.Equals(item.Kind, "keyword", StringComparison.OrdinalIgnoreCase))
        {
            rank -= 80;
        }

        string? preferredObject = string.IsNullOrWhiteSpace(resolvedObjectName) ? singleRelationName : resolvedObjectName;
        if (string.Equals(completionContext, "relation", StringComparison.OrdinalIgnoreCase))
        {
            if (IsRelationItem(item))
            {
                rank -= 1200;
            }
            else if (string.Equals(item.Kind, "column", StringComparison.OrdinalIgnoreCase))
            {
                rank += 1400;
            }
        }
        else if (string.Equals(completionContext, "member-column", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(item.Kind, "column", StringComparison.OrdinalIgnoreCase))
            {
                rank -= 1800;
                if (!string.IsNullOrWhiteSpace(preferredObject) && MatchesObject(item.SourceObject, item.Kind, selectedSchema, preferredObject))
                {
                    rank -= 600;
                }
            }
            else
            {
                rank += 2200;
            }
        }
        else if (string.Equals(completionContext, "column", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(completionContext, "select-column", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(item.Kind, "column", StringComparison.OrdinalIgnoreCase))
            {
                rank -= 900;
                if (!string.IsNullOrWhiteSpace(preferredObject) && MatchesObject(item.SourceObject, item.Kind, selectedSchema, preferredObject))
                {
                    rank -= 500;
                }
            }
            else if (string.Equals(item.Kind, "table", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.Kind, "view", StringComparison.OrdinalIgnoreCase))
            {
                rank += 600;
            }
        }

        return rank;
    }

    public static bool ContainsLocalizedText(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Any(static ch => ch > 127);
    }

    public static bool MatchesKey(string key, string normalizedPrefix)
    {
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return false;
        }

        string normalizedKey = key?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        if (normalizedKey.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string compactPrefix = BuildCompactMatchValue(normalizedPrefix);
        string compactKey = BuildCompactMatchValue(normalizedKey);
        if (ContainsLocalizedText(normalizedPrefix))
        {
            return normalizedKey.Contains(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(compactPrefix) && compactKey.Contains(compactPrefix, StringComparison.OrdinalIgnoreCase)) ||
                IsOrderedLocalizedMatch(compactKey, compactPrefix);
        }

        return normalizedPrefix.Length >= 2 &&
            (normalizedKey.Contains(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(compactPrefix) && compactKey.Contains(compactPrefix, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsRelationEntry(CompletionEntry item)
    {
        return string.Equals(item.Kind, "table", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Kind, "view", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Kind, "materializedview", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Kind, "synonym", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRelationItem(CompletionItem item)
    {
        return string.Equals(item.Kind, "table", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Kind, "view", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Kind, "materializedview", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Kind, "synonym", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsColumnEntry(CompletionEntry item)
    {
        return string.Equals(item.Kind, "column", StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesObject(string sourceObject, string kind, string selectedSchema, string objectName)
    {
        if (!TryParseSourceObject(sourceObject, kind, out string? schemaName, out string? objectNamePart, out _))
        {
            return false;
        }

        if (!string.Equals(objectNamePart, objectName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(selectedSchema) ||
            string.IsNullOrWhiteSpace(schemaName) ||
            string.Equals(schemaName, selectedSchema, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseSourceObject(string sourceObject, string kind, out string? schemaName, out string? objectName, out string? memberName)
    {
        schemaName = null;
        objectName = null;
        memberName = null;
        if (string.IsNullOrWhiteSpace(sourceObject))
        {
            return false;
        }

        string[] parts = sourceObject.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (string.Equals(kind, "column", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 3)
            {
                return false;
            }

            schemaName = parts[^3];
            objectName = parts[^2];
            memberName = parts[^1];
            return true;
        }

        if (parts.Length < 2)
        {
            return false;
        }

        schemaName = parts[^2];
        objectName = parts[^1];
        return true;
    }

    private static string BuildCompactMatchValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] buffer = value.Where(static ch => !char.IsWhiteSpace(ch)).ToArray();
        return buffer.Length == value.Length ? value : new string(buffer);
    }

    private static bool IsOrderedLocalizedMatch(string candidate, string prefix)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            string.IsNullOrWhiteSpace(prefix) ||
            !ContainsLocalizedText(prefix))
        {
            return false;
        }

        int candidateIndex = 0;
        foreach (char prefixChar in prefix)
        {
            if (char.IsWhiteSpace(prefixChar) || char.IsPunctuation(prefixChar) || char.IsSymbol(prefixChar))
            {
                continue;
            }

            bool found = false;
            while (candidateIndex < candidate.Length)
            {
                char candidateChar = candidate[candidateIndex++];
                if (char.Equals(candidateChar, prefixChar))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
