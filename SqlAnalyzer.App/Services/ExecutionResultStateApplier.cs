using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class ExecutionResultStateApplier
{
    public static ExecutionPlanViewItem? ApplyToResultSets(
        ObservableCollection<ResultSetViewItem> target,
        QueryExecutionResult result,
        bool includePlan,
        string providerName,
        UiTextSet uiText,
        Action<string>? diagnosticLog)
    {
        target.Clear();
        foreach (QueryResultSet resultSet in result.ResultSets)
        {
            target.Add(ResultSetViewItemFactory.Build(resultSet, providerName, uiText, diagnosticLog));
        }

        // 导航标题按最终数量生成，别在构造单个结果集时猜。
        ResultSetViewItemFactory.AssignNavigationTitles(target, uiText.Results);
        return includePlan ? ExecutionPlanViewItemBuilder.Build(target, providerName, uiText) : null;
    }

    public static void ApplyToState(
        DocumentExecutionState state,
        QueryExecutionResult result,
        IReadOnlyList<ResultSetViewItem> builtResultSets,
        bool includePlan,
        string providerName,
        UiTextSet uiText)
    {
        state.ResultSets.Clear();
        state.MessageText = string.Empty;
        state.LastError = result.Error;
        state.ValueDetail = null;
        state.IsValueDetailPanelOpen = false;
        state.IsEditingResult = false;

        // 新结果进来后，旧的单元格详情和编辑状态都不能再沿用。
        foreach (ResultSetViewItem builtResultSet in builtResultSets)
        {
            state.ResultSets.Add(builtResultSet);
        }

        state.SelectedResultSet = state.ResultSets.FirstOrDefault();
        state.ExecutionPlan = includePlan ? ExecutionPlanViewItemBuilder.Build(state.ResultSets, providerName, uiText) : null;
        state.RowCount = result.ResultSets.Sum(set => set.Rows.Count);
        state.DurationText = $"{result.Duration.TotalMilliseconds:0} ms";

        if (state.ResultSets.Count == 1 && state.ResultSets[0].IsMessageOnly)
        {
            state.MessageText = state.ResultSets[0].MessageText;
        }
        else if (result.Error != null)
        {
            state.MessageText = result.Error.Message;
        }

        state.ResetResultScrollOnNextRender = state.ResultSets.Any(ResultSetViewItemFactory.IsTabular);
    }
}
