using System.Collections.ObjectModel;
using System.Threading;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;

public sealed class DocumentExecutionState
{
    public ObservableCollection<ResultSetViewItem> ResultSets { get; } = [];
    public ObservableCollection<ResultWorkspaceTabItem> WorkspaceTabs { get; } = [];
    public ResultSetViewItem? SelectedResultSet { get; set; }
    public ResultWorkspaceTabItem? SelectedWorkspaceTab { get; set; }
    public ObservableCollection<string> AvailableSchemas { get; } = [];
    public string AvailableSchemasConnectionProfileId { get; set; } = string.Empty;
    public string SelectedSchema { get; set; } = string.Empty;
    public bool IsExecuting { get; set; }
    public string ExecutionStatus { get; set; } = string.Empty;
    public string ConnectionLabel { get; set; } = string.Empty;
    public string ConnectionForeground { get; set; } = "#9CA3AF";
    public string DurationText { get; set; } = "--";
    public string MessageText { get; set; } = string.Empty;
    public string LastExecutedSql { get; set; } = string.Empty;
    public int LastExecutedSqlBaseOffset { get; set; }
    public bool LastExecutionIncludedPlan { get; set; }
    public int PreviewRowLimit { get; set; } = 500;
    public QueryExecutionErrorInfo? LastError { get; set; }
    public ExecutionPlanViewItem? ExecutionPlan { get; set; }
    public ResultValueDetailState? ValueDetail { get; set; }
    public bool IsValueDetailPanelOpen { get; set; }
    public int RowCount { get; set; }
    public bool IsRenderingResults { get; set; }
    public bool ResetResultScrollOnNextRender { get; set; }
    public bool IsResultWorkspaceOpen { get; set; }
    public bool IsEditingResult { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}
