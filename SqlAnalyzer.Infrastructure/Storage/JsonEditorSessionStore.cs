using System.Text.Json;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.Infrastructure.Storage;

public sealed class JsonEditorSessionStore : IEditorSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly WorkspacePathResolver _paths;
    public JsonEditorSessionStore(WorkspacePathResolver paths)
    {
        _paths = paths;
    }
    public async Task<EditorSessionState> LoadAsync(CancellationToken cancellationToken = default)
    {
        string targetPath = _paths.SessionFilePath;
        if (!File.Exists(targetPath))
        {
            return new EditorSessionState();
        }

        try
        {
            return await LoadFromFileAsync(targetPath, cancellationToken);
        }
        catch (JsonException)
        {
            string backupPath = targetPath + ".bak";
            return File.Exists(backupPath)
                ? await LoadFromFileAsync(backupPath, cancellationToken)
                : new EditorSessionState();
        }
    }
    public async Task SaveAsync(EditorSessionState state, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        string targetPath = _paths.SessionFilePath;
        string temporaryPath = targetPath + ".tmp";
        string backupPath = targetPath + ".bak";

        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(targetPath))
        {
            File.Copy(targetPath, backupPath, overwrite: true);
        }

        File.Move(temporaryPath, targetPath, overwrite: true);
    }

    private static async Task<EditorSessionState> LoadFromFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EditorSessionState>(stream, JsonOptions, cancellationToken)
            ?? new EditorSessionState();
    }
}
