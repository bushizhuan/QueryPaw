using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class ExplorerActionController
{
    public ObjectNode? TryGetNode(object? sender)
    {
        return sender is MenuItem { Tag: ObjectNode node } ? node : null;
    }
    public string GetNodeNameToCopy(ObjectNode node)
    {
        return node.Name;
    }
    public async Task RefreshNodeAsync(MainWindowViewModel viewModel, ObjectNode node, CancellationToken cancellationToken = default)
    {
        await viewModel.RefreshNodeAsync(node, cancellationToken);
    }
    public async Task<ExplorerActionResult> ExportTableStructureAsync(MainWindowViewModel viewModel, ObjectNode node, CancellationToken cancellationToken = default)
    {
        string script = await viewModel.ExportTableStructureAsync(node, cancellationToken);
        return new ExplorerActionResult
        {
            SuggestedFileName = $"{node.Name}.sql",
            Content = script,
            ErrorTitle = viewModel.UiText.ExportTableStructureTitle
        };
    }
    public async Task<ExplorerActionResult> ExportTableDataAsync(MainWindowViewModel viewModel, ObjectNode node, CancellationToken cancellationToken = default)
    {
        string script = await viewModel.ExportTableDataAsync(node, cancellationToken);
        return new ExplorerActionResult
        {
            SuggestedFileName = $"{node.Name}.data.sql",
            Content = script,
            ErrorTitle = viewModel.UiText.ExportTableDataTitle
        };
    }
    public void OpenQueryForObject(MainWindowViewModel viewModel, ObjectNode node)
    {
        viewModel.OpenQueryForObject(node);
    }
    public void OpenQueryForObject(MainWindowViewModel viewModel, ObjectNode node, string templateKind)
    {
        viewModel.OpenQueryForObject(node, templateKind);
    }
    public async Task OpenTableDesignAsync(TableDesignCoordinator coordinator, Window owner, ObjectNode node)
    {
        await coordinator.OpenAsync(owner, node);
    }
    public EditorDocument? OpenObjectEditor(MainWindowViewModel viewModel, ObjectNode node)
    {
        return viewModel.OpenObjectEditorDocument(node);
    }
    public EditorDocument? OpenSchemaModelDiagram(MainWindowViewModel viewModel, ObjectNode node)
    {
        return viewModel.OpenModelDiagramDocument(node);
    }
    public EditorDocument? OpenTableModelDiagram(MainWindowViewModel viewModel, ObjectNode node)
    {
        return viewModel.OpenModelDiagramDocument(node);
    }
}
