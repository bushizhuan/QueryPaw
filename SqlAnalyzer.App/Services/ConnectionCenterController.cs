using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class ConnectionCenterController
{
    public void OpenDialog(MainWindowViewModel viewModel)
    {
        viewModel.ResetConnectionEditorToSelectedProfile();
        viewModel.ToggleConnectionDialog(true);
    }
    public void CloseDialog(MainWindowViewModel viewModel)
    {
        viewModel.CancelConnectionEditorDraft();
        viewModel.ToggleConnectionDialog(false);
    }
    public void CreateConnection(MainWindowViewModel viewModel)
    {
        viewModel.CreateConnectionProfile();
    }
    public void DuplicateSelectedConnection(MainWindowViewModel viewModel)
    {
        viewModel.BeginDuplicateConnectionDraft();
    }
    public void DeleteSelectedConnection(MainWindowViewModel viewModel)
    {
        viewModel.DeleteSelectedConnection();
    }
    public async Task SaveSelectedConnectionAsync(MainWindowViewModel viewModel, string? password)
    {
        CommitEditorPassword(viewModel, password);
        viewModel.SaveSelectedConnection();
        await viewModel.SaveConnectionsAsync();
    }
    public async Task<ConnectionCenterActionResult> SaveSelectedConnectionWithFeedbackAsync(MainWindowViewModel viewModel, string? password)
    {
        CommitEditorPassword(viewModel, password);
        bool saved = viewModel.SaveConnectionEditorDraft();
        if (saved)
        {
            await viewModel.SaveConnectionsAsync();
        }

        return new ConnectionCenterActionResult
        {
            Title = viewModel.UiText.SaveConnection,
            Message = saved ? viewModel.UiText.SaveConnectionSuccess : viewModel.UiText.NoConnectionSelected
        };
    }
    public async Task<string> TestSelectedConnectionAsync(MainWindowViewModel viewModel, string? password)
    {
        CommitEditorPassword(viewModel, password);
        if (!viewModel.ConnectionEditorCanTest)
        {
            return string.Empty;
        }

        return await viewModel.ValidateConnectionEditorProfileAsync();
    }
    public async Task UseSelectedConnectionAsync(MainWindowViewModel viewModel, string? password)
    {
        if (viewModel.SelectedConnectionProfile == null)
        {
            return;
        }

        await viewModel.SetActiveConnectionAsync(viewModel.SelectedConnectionProfile);
        CloseDialog(viewModel);
    }
    public void MergeImportedProfiles(MainWindowViewModel viewModel, IReadOnlyList<ConnectionProfile> profiles)
    {
        viewModel.MergeImportedProfiles(profiles);
    }
    public async Task ExportConnectionsAsync(MainWindowViewModel viewModel, string path, string? password, IReadOnlyList<ConnectionProfile>? selectedProfiles = null)
    {
        if (selectedProfiles == null)
        {
            await viewModel.ExportConnectionsAsync(path);
            return;
        }

        await viewModel.ExportConnectionsAsync(path, selectedProfiles);
    }
    public FilePickerOpenOptions BuildImportPickerOptions(MainWindowViewModel viewModel)
    {
        return new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = viewModel.UiText.ImportConnectionsDialogTitle,
            FileTypeFilter =
            [
                new FilePickerFileType("WLB files") { Patterns = ["*.wlb"] }
            ]
        };
    }
    public FilePickerSaveOptions BuildExportPickerOptions(MainWindowViewModel viewModel)
    {
        return new FilePickerSaveOptions
        {
            Title = viewModel.UiText.ExportConnectionsDialogTitle,
            SuggestedFileName = "connections.wlb",
            FileTypeChoices =
            [
                new FilePickerFileType("WLB files") { Patterns = ["*.wlb"] }
            ]
        };
    }
    public void CommitPassword(ConnectionProfile? profile, string? password)
    {
        if (profile != null)
        {
            profile.Password = password ?? string.Empty;
        }
    }
    public void CommitEditorPassword(MainWindowViewModel viewModel, string? password)
    {
        viewModel.CommitConnectionEditorPassword(password);
    }
    public ConnectionEditorState BuildEditorState(ConnectionProfile? profile, IReadOnlyList<DatabaseProviderDefinition> providers)
    {
        if (profile == null)
        {
            return new ConnectionEditorState();
        }

        DatabaseProviderDefinition? selectedProvider = providers.FirstOrDefault(item =>
            string.Equals(item.Name, profile.ProviderName, StringComparison.OrdinalIgnoreCase));

        return new ConnectionEditorState
        {
            Password = profile.Password,
            OracleConnectionMode = string.IsNullOrWhiteSpace(profile.OracleConnectionMode) ? "HostService" : profile.OracleConnectionMode,
            SelectedProvider = selectedProvider
        };
    }
    public void ApplyProviderSelection(ConnectionProfile profile, DatabaseProviderDefinition provider)
    {
        profile.ProviderName = provider.Name;
        profile.CapabilityLevel = provider.SupportLevel;

        if (string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            profile.OracleConnectionMode = string.IsNullOrWhiteSpace(profile.OracleConnectionMode) ? "HostService" : profile.OracleConnectionMode;
            profile.AuthenticationMode = string.IsNullOrWhiteSpace(profile.AuthenticationMode) ? "Default" : profile.AuthenticationMode;
            if (profile.OraclePort <= 0)
            {
                profile.OraclePort = 1521;
            }

            return;
        }

        if (profile.Port <= 0)
        {
            profile.Port = GetDefaultPort(provider.Name) ?? 0;
        }

        if (string.IsNullOrWhiteSpace(profile.AuthenticationMode))
        {
            profile.AuthenticationMode = "Default";
        }
    }
    public ConnectionEditorState HandleSelectionChanged(MainWindowViewModel viewModel)
    {
        viewModel.ResetConnectionEditorToSelectedProfile();
        viewModel.RefreshOracleUiState();
        return BuildEditorState(viewModel.ConnectionEditorDraft, viewModel.Providers);
    }
    public ConnectionEditorState? HandleProviderSelectionChanged(MainWindowViewModel viewModel, DatabaseProviderDefinition? provider)
    {
        if (viewModel.ConnectionEditorDraft == null || provider == null)
        {
            return null;
        }

        viewModel.ApplyConnectionEditorProvider(provider);
        viewModel.RefreshOracleUiState();
        return BuildEditorState(viewModel.ConnectionEditorDraft, viewModel.Providers);
    }
    private static int? GetDefaultPort(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "mysql" => 3306,
            "sqlserver" => 1433,
            "postgresql" => 5432,
            "kingbasees" => 54321,
            "dameng" => 5236,
            "mongodb" => 27017,
            _ => null
        };
    }
}
