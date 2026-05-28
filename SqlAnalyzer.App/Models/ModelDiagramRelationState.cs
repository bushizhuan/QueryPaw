using System.Collections.Generic;

namespace SqlAnalyzer.App.Models;

public sealed class ModelDiagramRelationState
{
    public string ConstraintName { get; set; } = string.Empty;

    public string FromTable { get; set; } = string.Empty;

    public string ToTable { get; set; } = string.Empty;

    public string ParentColumnsText { get; set; } = string.Empty;

    public string ChildColumnsText { get; set; } = string.Empty;

    public IReadOnlyList<string> ParentColumns { get; set; } = [];

    public IReadOnlyList<string> ChildColumns { get; set; } = [];

    public double StartX { get; set; }

    public double StartY { get; set; }

    public double EndX { get; set; }

    public double EndY { get; set; }

    public string PathData { get; set; } = string.Empty;

    public bool IsSelected { get; set; }

    public bool IsHighlighted { get; set; }

    public string Stroke => IsSelected ? "#2563EB" : IsHighlighted ? "#60A5FA" : "#94A3B8";

    public double Thickness => IsSelected ? 2.2d : IsHighlighted ? 1.8d : 1.2d;

    public string SummaryText => $"{FromTable} -> {ToTable}";
}
