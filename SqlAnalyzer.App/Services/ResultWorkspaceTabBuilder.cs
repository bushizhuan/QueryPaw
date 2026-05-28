using System.Linq;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Services;

public static class ResultWorkspaceTabBuilder
{
    public static void Rebuild(DocumentExecutionState state, UiTextSet uiText)
    {
        ResultWorkspaceTabItem? previousTab = state.SelectedWorkspaceTab;
        ResultSetViewItem? previousResultSet = state.SelectedResultSet;
        ResultSetViewItem? primaryResultSet = state.ResultSets.FirstOrDefault(item => !item.IsMessageOnly && item.Columns.Count > 0);

        state.WorkspaceTabs.Clear();
        if (primaryResultSet != null)
        {
            state.WorkspaceTabs.Add(new ResultWorkspaceTabItem
            {
                Header = uiText.Results,
                Kind = "result",
                ResultSet = primaryResultSet
            });
        }

        if (!string.IsNullOrWhiteSpace(state.MessageText))
        {
            state.WorkspaceTabs.Add(new ResultWorkspaceTabItem
            {
                Header = uiText.MessagesTab,
                Kind = "message"
            });
        }

        if (state.ExecutionPlan?.HasContent == true)
        {
            state.WorkspaceTabs.Add(new ResultWorkspaceTabItem
            {
                Header = uiText.Plan,
                Kind = "plan"
            });
        }

        ResultWorkspaceTabItem? selectedTab = ResolveSelectedTab(state, previousTab, previousResultSet);
        state.SelectedWorkspaceTab = selectedTab;

        if (selectedTab?.IsResultTab == true)
        {
            state.SelectedResultSet = CanSelectResultSet(previousResultSet, state)
                ? previousResultSet
                : primaryResultSet;
        }
        else if (state.SelectedResultSet == null && state.ResultSets.Count > 0)
        {
            state.SelectedResultSet = state.ResultSets[0];
        }
    }

    private static ResultWorkspaceTabItem? ResolveSelectedTab(
        DocumentExecutionState state,
        ResultWorkspaceTabItem? previousTab,
        ResultSetViewItem? previousResultSet)
    {
        ResultWorkspaceTabItem? selectedTab = null;
        if (previousTab != null)
        {
            if (previousTab.IsResultTab)
            {
                selectedTab = state.WorkspaceTabs.FirstOrDefault(item => item.IsResultTab);
            }
            else if (previousTab.IsMessageTab)
            {
                selectedTab = state.WorkspaceTabs.FirstOrDefault(item => item.IsMessageTab);
            }
            else if (previousTab.IsPlanTab)
            {
                selectedTab = state.WorkspaceTabs.FirstOrDefault(item => item.IsPlanTab);
            }
        }

        if (selectedTab == null && CanSelectResultSet(previousResultSet, state))
        {
            selectedTab = state.WorkspaceTabs.FirstOrDefault(item => item.IsResultTab);
        }

        return selectedTab ?? state.WorkspaceTabs.FirstOrDefault();
    }

    private static bool CanSelectResultSet(ResultSetViewItem? resultSet, DocumentExecutionState state)
    {
        return resultSet != null &&
            !resultSet.IsMessageOnly &&
            resultSet.Columns.Count > 0 &&
            state.ResultSets.Contains(resultSet);
    }
}
