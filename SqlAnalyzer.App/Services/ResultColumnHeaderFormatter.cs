using System;
using System.Collections.Generic;
using System.Linq;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Services;

public static class ResultColumnHeaderFormatter
{
    public static string Format(ResultColumnViewItem column, string headerMode)
    {
        string preferredName = ResolvePreferredDisplayName(column);
        return headerMode switch
        {
            "Raw" => ResolveRawName(column),
            "Display" => preferredName,
            _ => BuildDualHeader(column, preferredName)
        };
    }

    public static string FormatBody(ResultColumnViewItem column)
    {
        string preferredName = ResolvePreferredDisplayName(column);
        return string.IsNullOrWhiteSpace(preferredName) ? ResolveRawName(column) : preferredName;
    }

    public static string FormatTop(ResultColumnViewItem column)
    {
        return ResolveRawName(column);
    }

    public static string FormatBottom(ResultColumnViewItem column)
    {
        if (!string.IsNullOrWhiteSpace(column.CommentText))
        {
            return column.CommentText.Trim();
        }

        string preferredName = ResolvePreferredDisplayName(column);
        string rawName = ResolveRawName(column);
        return string.Equals(preferredName, rawName, StringComparison.OrdinalIgnoreCase) ? string.Empty : preferredName;
    }

    public static string FormatDisplay(ResultColumnViewItem column, string headerMode)
    {
        string text = Format(column, headerMode);
        return string.IsNullOrWhiteSpace(column.CommentText) || UsesCommentAsPrimaryHeader(column)
            ? text
            : text + Environment.NewLine + column.CommentText;
    }

    public static string BuildTooltip(ResultColumnViewItem column)
    {
        List<string> lines = new();
        if (!string.IsNullOrWhiteSpace(column.SourceTable))
        {
            lines.Add(column.SourceTable.Trim());
        }

        string body = FormatBody(column);
        if (!string.IsNullOrWhiteSpace(body))
        {
            lines.Add(body);
        }

        string rawName = ResolveRawName(column);
        if (!string.IsNullOrWhiteSpace(rawName) && !string.Equals(rawName, body, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(rawName);
        }

        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildDualHeader(ResultColumnViewItem column, string preferredDisplayName)
    {
        string rawName = ResolveRawName(column);
        string displayName = string.IsNullOrWhiteSpace(preferredDisplayName) ? rawName : preferredDisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return rawName;
        }

        if (string.Equals(displayName, rawName, StringComparison.OrdinalIgnoreCase))
        {
            return rawName;
        }

        return displayName + " (" + rawName + ")";
    }

    private static string ResolveRawName(ResultColumnViewItem column)
    {
        return string.IsNullOrWhiteSpace(column.RawName) ? column.HeaderText : column.RawName;
    }

    private static string ResolvePreferredDisplayName(ResultColumnViewItem column)
    {
        string rawName = ResolveRawName(column);
        if (column.HasExplicitAlias && !string.IsNullOrWhiteSpace(column.DisplayName))
        {
            return column.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(column.DisplayName) && !string.Equals(column.DisplayName, rawName, StringComparison.OrdinalIgnoreCase))
        {
            return column.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(column.CommentText))
        {
            return column.CommentText.Trim();
        }

        return rawName;
    }

    private static bool UsesCommentAsPrimaryHeader(ResultColumnViewItem column)
    {
        string rawName = ResolveRawName(column);
        return !column.HasExplicitAlias &&
            (string.IsNullOrWhiteSpace(column.DisplayName) || string.Equals(column.DisplayName, rawName, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(column.CommentText);
    }
}
