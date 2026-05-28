namespace SqlAnalyzer.App.Models;

public sealed class SearchPanelState
{
    public bool IsVisible { get; init; }

    public bool IsReplaceVisible { get; init; }

    public SearchPanelFocusTarget FocusTarget { get; init; }
}
