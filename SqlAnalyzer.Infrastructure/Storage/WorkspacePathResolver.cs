namespace SqlAnalyzer.Infrastructure.Storage;

public sealed class WorkspacePathResolver
{
    private readonly WorkspaceLayoutOptions _options;
    public WorkspacePathResolver(WorkspaceLayoutOptions options)
    {
        _options = options;
    }

    public string BaseDirectory => _options.BaseDirectory;
    public string ConnectionsFilePath => Path.Combine(BaseDirectory, _options.ConnectionsFileName);
    public string SessionFilePath => Path.Combine(BaseDirectory, _options.SessionFileName);
    public string LocalizationFilePath => Path.Combine(BaseDirectory, _options.LocalizationFileName);
    public void EnsureCreated()
    {
        Directory.CreateDirectory(BaseDirectory);
    }
}
