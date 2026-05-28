using System;
using System.Threading.Tasks;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class ExplorerOpenController
{
    public async Task<ExplorerOpenResult> OpenAsync(MainWindowViewModel viewModel, ObjectNode? node)
    {
        if (node == null)
        {
            return Failed(viewModel.UiText.MetadataLoadFailedTitle, viewModel.UiText.ExplorerNodeNotFoundMessage);
        }

        if (string.Equals(node.Type, "schema", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await viewModel.OpenSchemaAsync(node);
                return new ExplorerOpenResult
                {
                    Success = true,
                    Node = node
                };
            }
            catch (Exception ex)
            {
                return Failed("打开模式", ex.Message);
            }
        }

        if (!string.Equals(node.Type, "connection", StringComparison.OrdinalIgnoreCase))
        {
            return new ExplorerOpenResult
            {
                Success = true,
                Node = node
            };
        }

        ConnectionProfile? profile = viewModel.ResolveConnectionProfileForNode(node);
        if (profile == null)
        {
            return Failed(viewModel.UiText.MetadataLoadFailedTitle, viewModel.UiText.ExplorerConnectionResolveFailedMessage);
        }

        try
        {
            await viewModel.SetActiveConnectionAsync(profile);
            return new ExplorerOpenResult
            {
                Success = true,
                Node = node
            };
        }
        catch (Exception ex)
        {
            return Failed(viewModel.UiText.MetadataLoadFailedTitle, ex.Message);
        }
    }
    private static ExplorerOpenResult Failed(string title, string message)
    {
        return new ExplorerOpenResult
        {
            Success = false,
            Title = title,
            Message = message
        };
    }
}
