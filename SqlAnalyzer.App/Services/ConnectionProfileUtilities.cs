using System;
using System.Collections.Generic;
using System.Linq;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class ConnectionProfileUtilities
{
    public static int? GetDefaultPort(string? providerName)
    {
        return providerName?.ToLowerInvariant() switch
        {
            "oracle" => 1521,
            "mysql" => 3306,
            "sqlserver" => 1433,
            "postgresql" => 5432,
            "kingbasees" => 54321,
            "dameng" => 5236,
            "mongodb" => 27017,
            _ => null
        };
    }

    public static IReadOnlyList<string> GetAuthenticationModeOptions(string? providerName)
    {
        return providerName?.ToLowerInvariant() switch
        {
            "oracle" => new[] { "Default", "SYSDBA", "SYSOPER" },
            "sqlserver" => new[] { "Default", "IntegratedSecurity" },
            _ => new[] { "Default" }
        };
    }

    public static void NormalizeOracleSettings(ConnectionProfile profile)
    {
        if (!string.Equals(profile.ProviderName, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        profile.OracleConnectionMode = string.IsNullOrWhiteSpace(profile.OracleConnectionMode)
            ? "HostService"
            : profile.OracleConnectionMode;
        profile.OraclePort = profile.OraclePort <= 0 ? 1521 : profile.OraclePort;

        if (string.Equals(profile.OracleConnectionMode, "Tns", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(profile.OracleTnsName))
            {
                profile.OracleTnsName = profile.Server;
            }

            profile.Server = profile.OracleTnsName ?? string.Empty;
            profile.Database = profile.OracleServiceName ?? profile.Database;
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.OracleHost))
        {
            string endpoint = profile.Server ?? string.Empty;
            if (endpoint.Contains(':', StringComparison.Ordinal))
            {
                string[] parts = endpoint.Split(':');
                profile.OracleHost = parts[0];
                if (parts.Length > 1)
                {
                    string portText = parts[1].Split('/')[0];
                    if (int.TryParse(portText, out int port))
                    {
                        profile.OraclePort = port;
                    }
                }
            }
            else
            {
                profile.OracleHost = endpoint;
            }
        }

        if (string.IsNullOrWhiteSpace(profile.OracleServiceName))
        {
            profile.OracleServiceName = profile.Database;
        }

        profile.Server = profile.OracleHost ?? string.Empty;
        profile.Database = profile.OracleServiceName ?? string.Empty;
    }

    public static void CopyValues(ConnectionProfile target, ConnectionProfile source, bool preserveId)
    {
        string targetId = target.Id;
        target.Id = preserveId ? targetId : string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id;
        target.Name = source.Name ?? string.Empty;
        target.GroupName = source.GroupName ?? string.Empty;
        target.EnvironmentTag = string.IsNullOrWhiteSpace(source.EnvironmentTag) ? "DEV" : source.EnvironmentTag;
        target.IsFavorite = source.IsFavorite;
        target.LastUsedAt = source.LastUsedAt;
        target.Notes = source.Notes ?? string.Empty;
        target.ProviderName = source.ProviderName ?? string.Empty;
        target.CapabilityLevel = string.IsNullOrWhiteSpace(source.CapabilityLevel) ? "Experimental" : source.CapabilityLevel;
        target.RiskAccentBrush = source.RiskAccentBrush ?? "#94A3B8";
        target.EnvironmentBadgeBrush = source.EnvironmentBadgeBrush ?? "#F1F5F9";
        target.EnvironmentBadgeForeground = source.EnvironmentBadgeForeground ?? "#334155";
        target.LastUsedDisplay = source.LastUsedDisplay ?? string.Empty;
        target.OracleConnectionMode = source.OracleConnectionMode ?? "HostService";
        target.OracleHost = source.OracleHost ?? string.Empty;
        target.OraclePort = source.OraclePort <= 0 ? 1521 : source.OraclePort;
        target.OracleServiceName = source.OracleServiceName ?? string.Empty;
        target.OracleTnsName = source.OracleTnsName ?? string.Empty;
        target.Server = source.Server ?? string.Empty;
        target.Port = source.Port;
        target.Database = source.Database ?? string.Empty;
        target.Schema = source.Schema ?? string.Empty;
        target.UserName = source.UserName ?? string.Empty;
        target.AuthenticationMode = source.AuthenticationMode ?? string.Empty;
        target.AdvancedOptions = source.AdvancedOptions ?? string.Empty;
        target.ManagedDriverPath = source.ManagedDriverPath ?? string.Empty;
        target.NativeLibraryPath = source.NativeLibraryPath ?? string.Empty;
        target.SavePassword = source.SavePassword;
        target.EncryptedPassword = source.EncryptedPassword ?? string.Empty;
        target.Password = source.Password ?? string.Empty;
    }

    public static string BuildIdentityEndpoint(ConnectionProfile profile)
    {
        if (string.Equals(profile.ProviderName, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(profile.OracleConnectionMode, "Tns", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(profile.OracleTnsName) ? profile.Server : profile.OracleTnsName;
            }

            string host = string.IsNullOrWhiteSpace(profile.OracleHost) ? profile.Server : profile.OracleHost;
            return string.IsNullOrWhiteSpace(host) ? string.Empty : $"{host}:{profile.OraclePort}";
        }

        return profile.Port > 0 ? $"{profile.Server}:{profile.Port}" : profile.Server ?? string.Empty;
    }

    public static ConnectionProfile Clone(ConnectionProfile source)
    {
        return new ConnectionProfile
        {
            Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
            Name = source.Name ?? string.Empty,
            GroupName = source.GroupName ?? string.Empty,
            EnvironmentTag = string.IsNullOrWhiteSpace(source.EnvironmentTag) ? "DEV" : source.EnvironmentTag,
            IsFavorite = source.IsFavorite,
            LastUsedAt = source.LastUsedAt,
            Notes = source.Notes ?? string.Empty,
            ProviderName = source.ProviderName ?? string.Empty,
            CapabilityLevel = string.IsNullOrWhiteSpace(source.CapabilityLevel) ? "Experimental" : source.CapabilityLevel,
            RiskAccentBrush = source.RiskAccentBrush ?? "#94A3B8",
            EnvironmentBadgeBrush = source.EnvironmentBadgeBrush ?? "#F1F5F9",
            EnvironmentBadgeForeground = source.EnvironmentBadgeForeground ?? "#334155",
            LastUsedDisplay = source.LastUsedDisplay ?? string.Empty,
            OracleConnectionMode = source.OracleConnectionMode ?? "HostService",
            OracleHost = source.OracleHost ?? string.Empty,
            OraclePort = source.OraclePort <= 0 ? 1521 : source.OraclePort,
            OracleServiceName = source.OracleServiceName ?? string.Empty,
            OracleTnsName = source.OracleTnsName ?? string.Empty,
            Server = source.Server ?? string.Empty,
            Port = source.Port,
            Database = source.Database ?? string.Empty,
            Schema = source.Schema ?? string.Empty,
            UserName = source.UserName ?? string.Empty,
            AuthenticationMode = source.AuthenticationMode ?? string.Empty,
            AdvancedOptions = source.AdvancedOptions ?? string.Empty,
            ManagedDriverPath = source.ManagedDriverPath ?? string.Empty,
            NativeLibraryPath = source.NativeLibraryPath ?? string.Empty,
            SavePassword = source.SavePassword,
            EncryptedPassword = source.EncryptedPassword ?? string.Empty,
            Password = source.Password ?? string.Empty
        };
    }

    public static void ApplyVisuals(ConnectionProfile profile)
    {
        string environment = profile.EnvironmentTag?.Trim().ToUpperInvariant() ?? "DEV";
        (string accent, string badge, string foreground) = environment switch
        {
            "PROD" => ("#D14343", "#FEE2E2", "#991B1B"),
            "UAT" => ("#D97706", "#FEF3C7", "#92400E"),
            "TEST" => ("#0EA5E9", "#E0F2FE", "#0C4A6E"),
            _ => ("#16A34A", "#DCFCE7", "#166534")
        };

        profile.RiskAccentBrush = accent;
        profile.EnvironmentBadgeBrush = badge;
        profile.EnvironmentBadgeForeground = foreground;
        profile.LastUsedDisplay = profile.LastUsedAt.HasValue
            ? profile.LastUsedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : string.Empty;
    }

    public static void NormalizeEditorDraft(ConnectionProfile profile, int connectionCount, string allEnvironmentsLabel)
    {
        NormalizeOracleSettings(profile);
        ApplyVisuals(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = $"Connection {connectionCount + 1}";
        }

        if (string.IsNullOrWhiteSpace(profile.EnvironmentTag) ||
            string.Equals(profile.EnvironmentTag, allEnvironmentsLabel, StringComparison.OrdinalIgnoreCase))
        {
            profile.EnvironmentTag = "DEV";
        }
    }

    public static void EnsureUniqueId(ConnectionProfile profile, IEnumerable<ConnectionProfile> existingProfiles)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) ||
            existingProfiles.Any(item => string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase)))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }
    }

    public static void ApplyProviderSelection(ConnectionProfile profile, DatabaseProviderDefinition provider)
    {
        profile.ProviderName = provider.Name;
        profile.CapabilityLevel = provider.SupportLevel;

        if (string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            profile.OracleConnectionMode = string.IsNullOrWhiteSpace(profile.OracleConnectionMode)
                ? "HostService"
                : profile.OracleConnectionMode;
            profile.AuthenticationMode = string.IsNullOrWhiteSpace(profile.AuthenticationMode)
                ? "Default"
                : profile.AuthenticationMode;
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
}
