using Avalonia.Input;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.ViewModels;

namespace SqlAnalyzer.App.Services;

public sealed class SearchPanelController
{
    public SearchPanelState OpenReplace(MainWindowViewModel viewModel)
    {
        viewModel.ShowSearch(includeReplace: true);
        return new SearchPanelState
        {
            IsVisible = viewModel.IsSearchVisible,
            IsReplaceVisible = viewModel.IsReplaceVisible,
            FocusTarget = SearchPanelFocusTarget.Replace
        };
    }
    public SearchPanelState OpenFind(MainWindowViewModel viewModel)
    {
        viewModel.ShowSearch();
        return new SearchPanelState
        {
            IsVisible = viewModel.IsSearchVisible,
            IsReplaceVisible = viewModel.IsReplaceVisible,
            FocusTarget = SearchPanelFocusTarget.Search
        };
    }
    public SearchPanelState ToggleReplace(MainWindowViewModel viewModel)
    {
        viewModel.ToggleReplace();
        return new SearchPanelState
        {
            IsVisible = viewModel.IsSearchVisible,
            IsReplaceVisible = viewModel.IsReplaceVisible,
            FocusTarget = !viewModel.IsSearchVisible
                ? SearchPanelFocusTarget.Editor
                : viewModel.IsReplaceVisible
                    ? SearchPanelFocusTarget.Replace
                    : SearchPanelFocusTarget.Search
        };
    }
    public SearchPanelState Close(MainWindowViewModel viewModel)
    {
        viewModel.HideSearch();
        return new SearchPanelState
        {
            IsVisible = false,
            IsReplaceVisible = false,
            FocusTarget = SearchPanelFocusTarget.Editor
        };
    }
    public SearchPanelKeyAction GetKeyAction(Key key, bool replaceBox)
    {
        return key switch
        {
            Key.Enter when replaceBox => SearchPanelKeyAction.ReplaceCurrent,
            Key.Enter => SearchPanelKeyAction.FindNext,
            Key.Escape => SearchPanelKeyAction.Close,
            _ => SearchPanelKeyAction.None
        };
    }
    public SearchPanelState? HandleShortcut(MainWindowViewModel viewModel, Key key, KeyModifiers modifiers)
    {
        if (key == Key.F && modifiers.HasFlag(KeyModifiers.Control))
        {
            return OpenFind(viewModel);
        }

        if (key == Key.H && modifiers.HasFlag(KeyModifiers.Control))
        {
            return OpenReplace(viewModel);
        }

        return null;
    }
}
