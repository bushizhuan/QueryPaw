using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class DocumentLifecycleController
{
    public FilePickerOpenOptions BuildOpenPickerOptions(MainWindowViewModel viewModel)
    {
        return new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = viewModel.UiText.OpenFile
        };
    }
    public FilePickerSaveOptions BuildSavePickerOptions(MainWindowViewModel viewModel, string? currentPath, string currentTitle, bool saveAs)
    {
        return new FilePickerSaveOptions
        {
            Title = saveAs ? viewModel.UiText.SaveFileAs : viewModel.UiText.SaveFile,
            SuggestedFileName = string.IsNullOrWhiteSpace(currentPath) ? $"{currentTitle}.sql" : Path.GetFileName(currentPath),
            ShowOverwritePrompt = true
        };
    }
    public void ApplyOpenedDocument(MainWindowViewModel viewModel, string path, string content)
    {
        viewModel.CreateDocument(Path.GetFileName(path), content);
        if (viewModel.SelectedDocument == null)
        {
            return;
        }

        RecentFileEntry? recent = viewModel.RecentFiles.FirstOrDefault(item =>
            string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));

        viewModel.SelectedDocument.FilePath = path;
        if (recent != null)
        {
            viewModel.SelectedDocument.ConnectionProfileId = recent.ConnectionProfileId ?? string.Empty;
            viewModel.SelectedDocument.DefaultSchema = recent.DefaultSchema ?? string.Empty;
        }
        viewModel.SelectedDocument.IsDirty = false;
        viewModel.SelectedDocument.LastFileWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        viewModel.SyncSelectedDocumentBindingsFromDocument();
        viewModel.RegisterRecentFile(
            path,
            Path.GetFileName(path),
            viewModel.SelectedDocument.ConnectionProfileId,
            viewModel.SelectedDocument.DefaultSchema);
    }
    public void ApplySavedDocument(MainWindowViewModel viewModel, string path, string content)
    {
        if (viewModel.SelectedDocument == null)
        {
            return;
        }

        viewModel.SelectedDocument.FilePath = path;
        viewModel.SelectedDocument.Title = Path.GetFileName(path);
        viewModel.SelectedDocument.Content = content;
        viewModel.SelectedDocument.IsDirty = false;
        viewModel.SelectedDocument.LastFileWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        viewModel.RegisterRecentFile(
            path,
            viewModel.SelectedDocument.Title,
            viewModel.SelectedDocument.ConnectionProfileId,
            viewModel.SelectedDocument.DefaultSchema);
    }
    public async Task<bool> OpenDocumentAsync(MainWindowViewModel viewModel, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string content = await File.ReadAllTextAsync(path);
        ApplyOpenedDocument(viewModel, path, content);
        return true;
    }
    public async Task<bool> SaveDocumentAsync(MainWindowViewModel viewModel, string? path, string content)
    {
        if (viewModel.SelectedDocument == null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        await File.WriteAllTextAsync(path, content);
        ApplySavedDocument(viewModel, path, content);
        return true;
    }
    public IReadOnlyList<RecentFileMenuItemModel> BuildRecentFileMenuItems(MainWindowViewModel viewModel)
    {
        if (viewModel.RecentFiles.Count == 0)
        {
            return
            [
                new RecentFileMenuItemModel
                {
                    Header = viewModel.UiText.NoRecentFiles,
                    IsEnabled = false
                }
            ];
        }

        return viewModel.RecentFiles.Select(item => new RecentFileMenuItemModel
        {
            Header = $"{item.Title}  [{item.FilePath}]",
            FilePath = item.FilePath,
            IsEnabled = true
        }).ToArray();
    }
    public string? TryGetRecentFilePath(object? sender)
    {
        return sender is MenuItem { Tag: string path } && !string.IsNullOrWhiteSpace(path)
            ? path
            : null;
    }
    public async Task<bool> ReloadExternallyChangedDocumentsAsync(
        MainWindowViewModel viewModel,
        Action<string>? log = null)
    {
        bool selectedDocumentReloaded = false;

        foreach (EditorDocument document in viewModel.Documents.ToArray())
        {
            if (string.IsNullOrWhiteSpace(document.FilePath) || !File.Exists(document.FilePath))
            {
                continue;
            }

            DateTime currentWriteTimeUtc = File.GetLastWriteTimeUtc(document.FilePath);
            if (document.LastFileWriteTimeUtc == default || currentWriteTimeUtc <= document.LastFileWriteTimeUtc)
            {
                continue;
            }

            if (document.IsDirty)
            {
                log?.Invoke($"ExternalChangeSkipped: {document.FilePath}");
                continue;
            }

            string content = await File.ReadAllTextAsync(document.FilePath);
            document.Content = content;
            document.LastFileWriteTimeUtc = currentWriteTimeUtc;
            document.CaretOffset = Math.Min(document.CaretOffset, content.Length);
            log?.Invoke($"ExternalChangeReloaded: {document.FilePath}");

            if (ReferenceEquals(viewModel.SelectedDocument, document))
            {
                selectedDocumentReloaded = true;
            }
        }

        return selectedDocumentReloaded;
    }
}
