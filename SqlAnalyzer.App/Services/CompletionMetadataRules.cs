using System;
using System.Collections.Generic;
using System.Linq;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class CompletionMetadataRules
{
    public static IReadOnlyList<CompletionController.CompletionRelationReference> NormalizeRelationReferences(
        IReadOnlyList<CompletionController.CompletionRelationReference>? relationReferences)
    {
        if (relationReferences == null || relationReferences.Count == 0)
        {
            return Array.Empty<CompletionController.CompletionRelationReference>();
        }

        return relationReferences
            .Where(static item => item != null && !string.IsNullOrWhiteSpace(item.TableName))
            .GroupBy(static item => $"{item.SchemaName ?? string.Empty}.{item.TableName}.{item.Alias ?? string.Empty}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    public static bool ShouldUseRelationColumnSnapshots(
        string completionContext,
        IReadOnlyList<CompletionController.CompletionRelationReference> relationReferences)
    {
        return IsColumnContext(completionContext) && relationReferences.Count > 0;
    }

    public static bool ShouldLoadMetadata(
        IReadOnlyList<CompletionEntry> snapshot,
        string normalizedPrefix,
        string completionContext,
        bool allowEmptyPrefix,
        IReadOnlyList<CompletionController.CompletionRelationReference> relationReferences)
    {
        if (CompletionCandidateRules.ContainsLocalizedText(normalizedPrefix))
        {
            return IsContextSpecificCompletion(completionContext);
        }

        if (allowEmptyPrefix && IsColumnContext(completionContext))
        {
            return true;
        }

        return snapshot.Count == 0 && ShouldUseRelationColumnSnapshots(completionContext, relationReferences);
    }

    public static string ResolveRelationSchema(CompletionController.CompletionRelationReference relation, string preferredSchema)
    {
        return !string.IsNullOrWhiteSpace(relation.SchemaName) ? relation.SchemaName.Trim() : preferredSchema;
    }

    public static CompletionEntry CreateRelationScopedColumnEntry(
        CompletionEntry column,
        CompletionController.CompletionRelationReference relation,
        string relationSchema,
        bool includeRelationPrefix)
    {
        string insertPrefix = string.IsNullOrWhiteSpace(relation.Alias) ? relation.TableName : relation.Alias;
        string insertText = includeRelationPrefix && !string.IsNullOrWhiteSpace(insertPrefix)
            ? $"{insertPrefix}.{column.InsertText}"
            : column.InsertText;
        string relationName = string.IsNullOrWhiteSpace(relationSchema)
            ? relation.TableName
            : $"{relationSchema}.{relation.TableName}";
        string description = !string.IsNullOrWhiteSpace(relation.Alias)
            ? $"{relation.Alias} -> {relationName}"
            : relationName;

        return new CompletionEntry
        {
            DisplayText = column.DisplayText,
            InsertText = insertText,
            Kind = column.Kind,
            Description = description,
            MatchKeys = column.MatchKeys,
            SourceObject = column.SourceObject,
            SortWeight = column.SortWeight
        };
    }

    public static CompletionItem CreateItem(CompletionEntry item, string completionContext = "")
    {
        return new CompletionItem
        {
            DisplayText = item.DisplayText,
            InsertText = ResolveInsertText(item, completionContext),
            Kind = item.Kind,
            Description = item.Description,
            MatchKeys = item.MatchKeys,
            SourceObject = item.SourceObject,
            SortWeight = item.SortWeight
        };
    }

    public static string? ResolvePreferredObject(string completionContext, string? resolvedObjectName, string? singleRelationName)
    {
        if (!IsColumnContext(completionContext))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(resolvedObjectName) ? singleRelationName : resolvedObjectName;
    }

    public static bool IsRelationContext(string completionContext)
    {
        return string.Equals(completionContext, "relation", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsColumnContext(string completionContext)
    {
        return string.Equals(completionContext, "column", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(completionContext, "select-column", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(completionContext, "member-column", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsContextSpecificCompletion(string completionContext)
    {
        return IsRelationContext(completionContext) || IsColumnContext(completionContext);
    }

    public static bool ShouldUseObjectColumnSnapshot(string completionContext, string? preferredObject)
    {
        return IsColumnContext(completionContext) && !string.IsNullOrWhiteSpace(preferredObject);
    }

    public static IEnumerable<CompletionEntry> FilterEntries(
        IReadOnlyList<CompletionEntry> source,
        string normalizedPrefix,
        string selectedSchema,
        string completionContext,
        string? resolvedObjectName,
        string? singleRelationName,
        bool allowEmptyPrefix)
    {
        IEnumerable<CompletionEntry> filtered = source;
        if (string.Equals(completionContext, "relation", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(CompletionCandidateRules.IsRelationEntry);
        }
        else if (string.Equals(completionContext, "member-column", StringComparison.OrdinalIgnoreCase))
        {
            filtered = FilterColumnsByObject(source, selectedSchema, resolvedObjectName);
        }
        else if (string.Equals(completionContext, "column", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(completionContext, "select-column", StringComparison.OrdinalIgnoreCase))
        {
            string? preferredObject = string.IsNullOrWhiteSpace(resolvedObjectName) ? singleRelationName : resolvedObjectName;
            filtered = string.IsNullOrWhiteSpace(preferredObject)
                ? source.Where(CompletionCandidateRules.IsColumnEntry)
                : FilterColumnsByObject(source, selectedSchema, preferredObject);
        }

        if (allowEmptyPrefix && string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return filtered;
        }

        return filtered.Where(item => item.MatchKeys.Any(key => CompletionCandidateRules.MatchesKey(key, normalizedPrefix)));
    }

    private static string ResolveInsertText(CompletionEntry item, string completionContext)
    {
        if (!string.Equals(completionContext, "member-column", StringComparison.OrdinalIgnoreCase))
        {
            return item.InsertText;
        }

        return CompletionCandidateRules.TryParseSourceObject(item.SourceObject, item.Kind, out _, out _, out string? memberName) &&
            !string.IsNullOrWhiteSpace(memberName)
            ? memberName
            : item.InsertText.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? item.InsertText;
    }

    private static IEnumerable<CompletionEntry> FilterColumnsByObject(IEnumerable<CompletionEntry> source, string selectedSchema, string? objectName)
    {
        IEnumerable<CompletionEntry> columns = source.Where(CompletionCandidateRules.IsColumnEntry);
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return columns;
        }

        List<CompletionEntry> scoped = columns
            .Where(item => CompletionCandidateRules.MatchesObject(item.SourceObject, item.Kind, selectedSchema, objectName))
            .ToList();
        return scoped.Count > 0 ? scoped : columns;
    }
}
