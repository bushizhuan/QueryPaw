using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;

public sealed class ConnectionEditorState
{
    public string Password { get; init; } = string.Empty;
    public string OracleConnectionMode { get; init; } = "HostService";
    public DatabaseProviderDefinition? SelectedProvider { get; init; }
}
