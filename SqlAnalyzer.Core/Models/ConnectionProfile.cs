using System.Text.Json.Serialization;

namespace SqlAnalyzer.Core.Models;

public sealed class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string EnvironmentTag { get; set; } = "DEV";
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string CapabilityLevel { get; set; } = "Experimental";
    public string RiskAccentBrush { get; set; } = "#94A3B8";
    public string EnvironmentBadgeBrush { get; set; } = "#F1F5F9";
    public string EnvironmentBadgeForeground { get; set; } = "#334155";
    public string LastUsedDisplay { get; set; } = string.Empty;
    public string OracleConnectionMode { get; set; } = "HostService";
    public string OracleHost { get; set; } = string.Empty;
    public int OraclePort { get; set; } = 1521;
    public string OracleServiceName { get; set; } = string.Empty;
    public string OracleTnsName { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Database { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AuthenticationMode { get; set; } = "Default";
    public string AdvancedOptions { get; set; } = string.Empty;
    public string ManagedDriverPath { get; set; } = string.Empty;
    public string NativeLibraryPath { get; set; } = string.Empty;
    public bool SavePassword { get; set; }
    public string? EncryptedPassword { get; set; }

    [JsonIgnore]
    public string Password { get; set; } = string.Empty;
}
