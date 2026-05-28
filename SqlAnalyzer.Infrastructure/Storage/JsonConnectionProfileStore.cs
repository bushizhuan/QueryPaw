using System.Text.Json;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.Infrastructure.Storage;

public sealed class JsonConnectionProfileStore : IConnectionProfileStore
{
    private const string FileVersion = "1";
    private const long MaxTransferFileBytes = 10L * 1024L * 1024L;
    private const int MaxProfileCount = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly WorkspacePathResolver _paths;
    public JsonConnectionProfileStore(WorkspacePathResolver paths)
    {
        _paths = paths;
    }
    public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        return LoadFromPathAsync(_paths.ConnectionsFilePath, cancellationToken);
    }
    public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        return SaveToPathAsync(_paths.ConnectionsFilePath, profiles, includePasswords: true, portable: false, cancellationToken);
    }
    public async Task ExportAsync(string filePath, IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await SaveToPathAsync(filePath, profiles, includePasswords: false, portable: true, cancellationToken);
    }
    public Task<IReadOnlyList<ConnectionProfile>> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return LoadFromPathAsync(filePath, cancellationToken);
    }
    private static async Task SaveToPathAsync(
        string filePath,
        IReadOnlyList<ConnectionProfile> profiles,
        bool includePasswords,
        bool portable,
        CancellationToken cancellationToken)
    {
        if (profiles.Count > MaxProfileCount)
        {
            throw new InvalidDataException($"连接数量超过上限 {MaxProfileCount}，请拆分后再导出。");
        }

        string payload = JsonSerializer.Serialize(
            profiles.Select(profile => PrepareForSave(profile, includePasswords)).ToArray(),
            JsonOptions);

        ConnectionProfileEnvelope envelope = new()
        {
            Version = FileVersion,
            Payload = portable
                ? ConnectionProfileCrypto.EncodePortable(payload)
                : ConnectionProfileCrypto.Encrypt(payload)
        };

        await using FileStream stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken);
    }
    private static async Task<IReadOnlyList<ConnectionProfile>> LoadFromPathAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<ConnectionProfile>();
        }

        FileInfo fileInfo = new(filePath);
        if (fileInfo.Length > MaxTransferFileBytes)
        {
            throw new InvalidDataException($"连接文件超过上限 {MaxTransferFileBytes / 1024 / 1024}MB，已拒绝加载。");
        }

        await using FileStream stream = File.OpenRead(filePath);
        try
        {
            ConnectionProfileEnvelope? envelope = await JsonSerializer.DeserializeAsync<ConnectionProfileEnvelope>(stream, JsonOptions, cancellationToken);
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Payload))
            {
                return Array.Empty<ConnectionProfile>();
            }

            string payload = ConnectionProfileCrypto.Decrypt(envelope.Payload);
            List<ConnectionProfile>? profiles = JsonSerializer.Deserialize<List<ConnectionProfile>>(payload, JsonOptions);
            return PrepareLoadedProfiles(profiles);
        }
        catch (JsonException)
        {
            stream.Position = 0;
            List<ConnectionProfile>? legacyProfiles = await JsonSerializer.DeserializeAsync<List<ConnectionProfile>>(stream, JsonOptions, cancellationToken);
            return PrepareLoadedProfiles(legacyProfiles);
        }
    }

    private static IReadOnlyList<ConnectionProfile> PrepareLoadedProfiles(IEnumerable<ConnectionProfile>? profiles)
    {
        if (profiles == null)
        {
            return Array.Empty<ConnectionProfile>();
        }

        List<ConnectionProfile> result = [];
        foreach (ConnectionProfile profile in profiles)
        {
            if (result.Count >= MaxProfileCount)
            {
                throw new InvalidDataException($"连接数量超过上限 {MaxProfileCount}，已停止导入。");
            }

            if (profile != null)
            {
                result.Add(PrepareForLoad(profile));
            }
        }

        return result.ToArray();
    }
    private static ConnectionProfile PrepareForSave(ConnectionProfile profile, bool includePasswords)
    {
        ConnectionProfile clone = Clone(profile);
        if (!includePasswords || !profile.SavePassword)
        {
            clone.SavePassword = false;
            clone.EncryptedPassword = string.Empty;
            clone.Password = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(profile.Password))
        {
            clone.EncryptedPassword = ConnectionProfileCrypto.Encrypt(profile.Password);
        }
        else if (!string.IsNullOrWhiteSpace(profile.EncryptedPassword))
        {
            clone.EncryptedPassword = profile.EncryptedPassword;
        }

        return clone;
    }
    private static ConnectionProfile PrepareForLoad(ConnectionProfile profile)
    {
        ConnectionProfile clone = Clone(profile);
        try
        {
            clone.Password = string.IsNullOrWhiteSpace(profile.EncryptedPassword)
                ? string.Empty
                : ConnectionProfileCrypto.Decrypt(profile.EncryptedPassword);
        }
        catch
        {
            clone.Password = string.Empty;
        }
        return clone;
    }
    private static ConnectionProfile Clone(ConnectionProfile profile)
    {
        return new ConnectionProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            GroupName = profile.GroupName,
            EnvironmentTag = profile.EnvironmentTag,
            IsFavorite = profile.IsFavorite,
            LastUsedAt = profile.LastUsedAt,
            Notes = profile.Notes,
            ProviderName = profile.ProviderName,
            CapabilityLevel = profile.CapabilityLevel,
            RiskAccentBrush = profile.RiskAccentBrush,
            EnvironmentBadgeBrush = profile.EnvironmentBadgeBrush,
            EnvironmentBadgeForeground = profile.EnvironmentBadgeForeground,
            LastUsedDisplay = profile.LastUsedDisplay,
            OracleConnectionMode = profile.OracleConnectionMode,
            OracleHost = profile.OracleHost,
            OraclePort = profile.OraclePort,
            OracleServiceName = profile.OracleServiceName,
            OracleTnsName = profile.OracleTnsName,
            Server = profile.Server,
            Port = profile.Port,
            Database = profile.Database,
            Schema = profile.Schema,
            UserName = profile.UserName,
            AuthenticationMode = profile.AuthenticationMode,
            AdvancedOptions = profile.AdvancedOptions,
            ManagedDriverPath = profile.ManagedDriverPath,
            NativeLibraryPath = profile.NativeLibraryPath,
            SavePassword = profile.SavePassword,
            EncryptedPassword = profile.EncryptedPassword,
            Password = profile.Password
        };
    }

    private sealed class ConnectionProfileEnvelope
    {
        public string Version { get; set; } = FileVersion;
        public string Payload { get; set; } = string.Empty;
    }
}
