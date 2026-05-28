using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.App.Views;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class ObjectDetailsCoordinator
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Action<string> _log;
    private readonly Func<string, string, Task> _messageBox;
    public ObjectDetailsCoordinator(
        MainWindowViewModel viewModel,
        Action<string> log,
        Func<string, string, Task> messageBox)
    {
        _viewModel = viewModel;
        _log = log;
        _messageBox = messageBox;
    }
    public async Task OpenAsync(Window owner, ObjectNode node, CancellationToken cancellationToken = default)
    {
        try
        {
            ObjectDetailsModel model = await BuildModelAsync(node, cancellationToken);
            ObjectDetailsWindow window = new(model, _viewModel.UiText);
            await window.ShowDialog(owner);
        }
        catch (Exception ex)
        {
            _log($"ObjectDetails:open-failed; node={node.SchemaName}.{node.Name}; ex={ex}");
            await _messageBox(_viewModel.UiText.ObjectDetails, ex.Message);
        }
    }
    private async Task<ObjectDetailsModel> BuildModelAsync(ObjectNode node, CancellationToken cancellationToken)
    {
        string ddlText = string.Empty;
        if (node.CanExportTableStructure)
        {
            try
            {
                ddlText = await _viewModel.ExportTableStructureAsync(node, cancellationToken);
            }
            catch (Exception ex)
            {
                _log($"ObjectDetails:ddl-failed; node={node.SchemaName}.{node.Name}; message={ex.Message}");
                ddlText = string.Format(_viewModel.UiText.ObjectDetailsDdlLoadFailedFormat, ex.Message);
            }
        }

        return new ObjectDetailsModel
        {
            WindowTitle = $"{_viewModel.UiText.ObjectDetails} - {node.DisplayText}",
            DisplayName = node.DisplayName,
            RawName = node.Name,
            DisplayText = node.DisplayText,
            SchemaName = node.SchemaName,
            ObjectType = node.Type,
            ProviderName = node.ProviderName,
            Description = node.Description,
            DdlText = ddlText,
            PreviewSql = BuildPreviewSql(node)
        };
    }
    private static string BuildPreviewSql(ObjectNode node)
    {
        if (string.Equals(node.Type, "table", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Type, "view", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Type, "materializedview", StringComparison.OrdinalIgnoreCase))
        {
            string target = string.IsNullOrWhiteSpace(node.SchemaName)
                ? node.Name
                : $"{node.SchemaName}.{node.Name}";
            return $"select * from {target};";
        }

        return string.Empty;
    }
}
