using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.App.Views;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class TableDesignCoordinator
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Action<string> _log;
    private readonly Func<string, string, Task> _messageBox;
    public TableDesignCoordinator(
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
        TableDesignModel originalDesign;
        try
        {
            originalDesign = await _viewModel.LoadTableDesignAsync(node, cancellationToken);
            _log(
                $"TableDesign:loaded; node={node.SchemaName}.{node.Name}; columns={originalDesign.Columns.Count}; indexes={originalDesign.Indexes.Count}; uniqueKeys={originalDesign.UniqueKeys.Count}; foreignKeys={originalDesign.ForeignKeys.Count}; checks={originalDesign.Checks.Count}; triggers={originalDesign.Triggers.Count}; options={originalDesign.Options.Count}");
        }
        catch (Exception ex)
        {
            _log($"TableDesign:load-failed; node={node.SchemaName}.{node.Name}; ex={ex}");
            originalDesign = BuildFallbackTableDesign(node);
        }

        try
        {
            TableDesignerWindow designerWindow = new(originalDesign);
            TableDesignModel? updatedDesign = await designerWindow.ShowDialog<TableDesignModel?>(owner);
            if (updatedDesign == null || !originalDesign.SupportsDirectSave)
            {
                return;
            }

            await _viewModel.SaveTableDesignAsync(node, originalDesign, updatedDesign, cancellationToken);
            await _viewModel.RefreshNodeAsync(node);
        }
        catch (Exception ex)
        {
            _log($"TableDesign:window-failed; node={node.SchemaName}.{node.Name}; ex={ex}");
            await _messageBox(_viewModel.UiText.TableDesign, ex.Message);
        }
    }
    private static TableDesignModel BuildFallbackTableDesign(ObjectNode node)
    {
        return new TableDesignModel
        {
            ProviderName = node.ProviderName ?? string.Empty,
            SchemaName = node.SchemaName ?? string.Empty,
            TableName = node.Name ?? string.Empty,
            TableComment = string.Empty,
            SupportsDirectSave = false,
            CapabilityLevel = "PreviewOnly",
            Columns = [],
            Indexes = [],
            UniqueKeys = [],
            ForeignKeys = [],
            Checks = [],
            Triggers = [],
            Options =
            [
                new TableOptionDefinition { Name = "Object", Value = node.Name ?? string.Empty },
                new TableOptionDefinition { Name = "Schema", Value = node.SchemaName ?? string.Empty },
                new TableOptionDefinition { Name = "Mode", Value = "PreviewOnly" }
            ]
        };
    }
}
