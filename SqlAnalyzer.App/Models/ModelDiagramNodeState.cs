using System.Collections.Generic;

namespace SqlAnalyzer.App.Models;

public sealed class ModelDiagramNodeState
{
    public string TableName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string CommentText { get; set; } = string.Empty;

    public IReadOnlyList<string> PreviewColumnLines { get; set; } = [];

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 240d;

    public double Height { get; set; } = 84d;

    public bool IsSelected { get; set; }

    public bool IsHighlighted { get; set; }

    public bool IsSearchMatched { get; set; }

    public string Background => IsSelected ? "#DBEAFE" : IsHighlighted ? "#EFF6FF" : IsSearchMatched ? "#FFFBEA" : "#FFFFFF";

    public string BorderBrush => IsSelected ? "#2563EB" : IsHighlighted ? "#60A5FA" : IsSearchMatched ? "#F59E0B" : "#CBD5E1";

    public string TitleForeground => IsSelected ? "#1D4ED8" : "#0F172A";
}
