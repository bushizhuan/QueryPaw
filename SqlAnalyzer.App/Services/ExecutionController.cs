using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SqlAnalyzer.App.ViewModels;

namespace SqlAnalyzer.App.Services;

public sealed class ExecutionController
{
    public void CancelExecution(MainWindowViewModel viewModel, Action<string>? appendUiLog = null)
    {
        string documentTitle = viewModel.SelectedDocument?.Title ?? "(no-document)";
        appendUiLog?.Invoke($"ExecuteEditorTextAsync:cancel-requested; doc={documentTitle}");
        viewModel.CancelSelectedDocumentExecution();
    }
    public async Task ExecuteAndRenderAsync(
        MainWindowViewModel viewModel,
        string sql,
        bool includePlan,
        Action<string> appendUiLog,
        Func<Task> renderResultsAsync,
        int sqlBaseOffset = 0,
        int maxPreviewRows = MainWindowViewModel.DefaultResultPreviewRowLimit,
        CancellationToken cancellationToken = default)
    {
        bool executed = await ExecuteAsync(viewModel, sql, includePlan, appendUiLog, sqlBaseOffset, maxPreviewRows, cancellationToken);
        if (executed)
        {
            await renderResultsAsync();
        }
    }
    public async Task<bool> ExecuteAsync(
        MainWindowViewModel viewModel,
        string sql,
        bool includePlan,
        Action<string> appendUiLog,
        int sqlBaseOffset = 0,
        int maxPreviewRows = MainWindowViewModel.DefaultResultPreviewRowLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        if (!viewModel.IsSelectedDocumentConnectionLinked())
        {
            string blockedDocumentTitle = viewModel.SelectedDocument?.Title ?? "(no-document)";
            string blockedConnectionName = viewModel.SelectedDocumentConnectionProfile?.Name ?? "(no-connection)";
            appendUiLog($"ExecuteEditorTextAsync:blocked-not-linked; doc={blockedDocumentTitle}; connection={blockedConnectionName}");
            viewModel.BlockSelectedDocumentExecutionBecauseConnectionNotLinked();
            return false;
        }

        string documentTitle = viewModel.SelectedDocument?.Title ?? "(no-document)";
        string connectionName = viewModel.SelectedDocumentConnectionProfile?.Name ?? "(no-connection)";
        Stopwatch executeStopwatch = Stopwatch.StartNew();

        appendUiLog($"ExecuteEditorTextAsync:start; doc={documentTitle}; connection={connectionName}; includePlan={includePlan}; sqlLength={sql.Length}");

        using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        viewModel.BeginSelectedDocumentExecution(cancellationTokenSource);
        try
        {
            await viewModel.ExecuteSelectedDocumentAsync(sql, includePlan, sqlBaseOffset, maxPreviewRows, cancellationTokenSource.Token);
            appendUiLog($"ExecuteEditorTextAsync:service-complete; doc={documentTitle}; elapsedMs={executeStopwatch.ElapsedMilliseconds}");
            return true;
        }
        catch (OperationCanceledException)
        {
            appendUiLog($"ExecuteEditorTextAsync:cancelled; doc={documentTitle}; elapsedMs={executeStopwatch.ElapsedMilliseconds}");
            viewModel.RecordSelectedDocumentExecutionHistory(sql, includePlan, isSuccess: false, "Execution cancelled.", 0, $"{executeStopwatch.ElapsedMilliseconds} ms");
            return false;
        }
        catch (Exception ex)
        {
            appendUiLog($"ExecuteEditorTextAsync:error; doc={documentTitle}; elapsedMs={executeStopwatch.ElapsedMilliseconds}; error={ex}");
            viewModel.RecordSelectedDocumentExecutionHistory(sql, includePlan, isSuccess: false, ex.Message, 0, $"{executeStopwatch.ElapsedMilliseconds} ms");
            throw;
        }
        finally
        {
            viewModel.CompleteSelectedDocumentExecution();
            appendUiLog($"ExecuteEditorTextAsync:complete; doc={documentTitle}; elapsedMs={executeStopwatch.ElapsedMilliseconds}");
        }
    }
}
