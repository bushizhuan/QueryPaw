using System;
using System.Collections.Generic;

namespace SqlAnalyzer.App.Models;

public sealed class CompletionItem
{
    public string DisplayText { get; set; } = string.Empty;
    public string InsertText { get; set; } = string.Empty;
    public string Kind { get; set; } = "keyword";
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<string> MatchKeys { get; set; } = Array.Empty<string>();
    public string SourceObject { get; set; } = string.Empty;
    public int SortWeight { get; set; }

    public string PrimaryText => string.IsNullOrWhiteSpace(InsertText) ? Text : InsertText;

    public string SecondaryText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayText) &&
                !string.Equals(DisplayText, InsertText, StringComparison.OrdinalIgnoreCase) &&
                !InsertText.EndsWith("." + DisplayText, StringComparison.OrdinalIgnoreCase))
            {
                return DisplayText;
            }

            if (string.Equals(Kind, "keyword", StringComparison.OrdinalIgnoreCase))
            {
                return Description;
            }

            return string.Empty;
        }
    }

    public string ContextText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Description) &&
                !string.Equals(Description, SecondaryText, StringComparison.Ordinal))
            {
                return Description;
            }

            return string.Empty;
        }
    }

    public bool HasSecondaryText => !string.IsNullOrWhiteSpace(SecondaryText);

    public bool HasContextText => !string.IsNullOrWhiteSpace(ContextText);

    public string PopupToolTip
    {
        get
        {
            List<string> parts = new(3);
            if (!string.IsNullOrWhiteSpace(PrimaryText))
            {
                parts.Add(PrimaryText);
            }

            if (HasSecondaryText)
            {
                parts.Add(SecondaryText);
            }

            if (HasContextText)
            {
                parts.Add(ContextText);
            }

            return string.Join(Environment.NewLine, parts);
        }
    }

    public string KindBadgeText =>
        Kind?.ToLowerInvariant() switch
        {
            "table" => "TAB",
            "view" => "VIEW",
            "column" => "COL",
            "function" => "FUNC",
            "snippet" => "SNIP",
            _ => "SQL"
        };

    public string KindBrush =>
        Kind?.ToLowerInvariant() switch
        {
            "table" => "#DBEAFE",
            "view" => "#E0F2FE",
            "column" => "#ECFCCB",
            "function" => "#FCE7F3",
            "snippet" => "#FEF3C7",
            _ => "#EEF2FF"
        };

    public string KindForeground =>
        Kind?.ToLowerInvariant() switch
        {
            "table" => "#1D4ED8",
            "view" => "#0369A1",
            "column" => "#3F6212",
            "function" => "#9D174D",
            "snippet" => "#92400E",
            _ => "#374151"
        };

    public string Text
    {
        get => string.IsNullOrWhiteSpace(DisplayText) ? InsertText : DisplayText;
        set
        {
            DisplayText = value;
            if (string.IsNullOrWhiteSpace(InsertText))
            {
                InsertText = value;
            }
        }
    }
}
