using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SqlAnalyzer.Core.Models;
using StackExchange.Redis;

namespace SqlAnalyzer.Data.Redis;

internal sealed record RedisConnectionSettings(
    ConfigurationOptions Options,
    int DatabaseIndex,
    string CacheKey,
    string EndpointDisplay);

internal sealed class RedisConnectionOptionsBuilder
{
    private const int DefaultRedisPort = 6379;

    public RedisConnectionSettings Build(ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Server))
        {
            throw new InvalidOperationException("Redis host is required.");
        }

        int databaseIndex = ParseDatabaseIndex(profile.Database);
        int port = profile.Port > 0 ? profile.Port : DefaultRedisPort;
        ConfigurationOptions options = BuildBaseOptions(profile.AdvancedOptions);
        options.EndPoints.Clear();

        foreach (string endpoint in SplitEndpoints(profile.Server))
        {
            AddEndpoint(options, endpoint, port);
        }

        if (options.EndPoints.Count == 0)
        {
            throw new InvalidOperationException("Redis host is required.");
        }

        string userName = profile.UserName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(userName) &&
            !string.Equals(userName, "default", StringComparison.OrdinalIgnoreCase))
        {
            options.User = userName;
        }

        if (!string.IsNullOrEmpty(profile.Password))
        {
            options.Password = profile.Password;
        }

        options.ClientName = string.IsNullOrWhiteSpace(options.ClientName) ? "QueryPaw" : options.ClientName;
        options.AbortOnConnectFail = false;
        options.AllowAdmin = true;

        string endpointDisplay = string.Join(", ", options.EndPoints.Select(item => item.ToString()));
        string cacheKey = BuildCacheKey(options, databaseIndex, profile.Password, profile.AdvancedOptions);
        return new RedisConnectionSettings(options, databaseIndex, cacheKey, endpointDisplay);
    }

    private static ConfigurationOptions BuildBaseOptions(string advancedOptions)
    {
        if (string.IsNullOrWhiteSpace(advancedOptions))
        {
            return new ConfigurationOptions();
        }

        string normalizedOptions = advancedOptions.Trim().Trim(',').Replace(";", ",", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalizedOptions))
        {
            return new ConfigurationOptions();
        }

        try
        {
            return ConfigurationOptions.Parse(normalizedOptions, ignoreUnknown: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Redis advanced options are invalid: " + ex.Message, ex);
        }
    }

    private static int ParseDatabaseIndex(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return 0;
        }

        if (int.TryParse(database.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int databaseIndex) &&
            databaseIndex >= 0)
        {
            return databaseIndex;
        }

        throw new InvalidOperationException("Redis DB Index must be a non-negative integer.");
    }

    private static IEnumerable<string> SplitEndpoints(string server)
    {
        return server
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private static void AddEndpoint(ConfigurationOptions options, string endpoint, int defaultPort)
    {
        string normalizedEndpoint = endpoint.Trim();
        int separatorIndex = normalizedEndpoint.LastIndexOf(':');
        if (separatorIndex > 0 &&
            separatorIndex < normalizedEndpoint.Length - 1 &&
            int.TryParse(normalizedEndpoint[(separatorIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int endpointPort))
        {
            string host = normalizedEndpoint[..separatorIndex];
            options.EndPoints.Add(host, endpointPort);
            return;
        }

        options.EndPoints.Add(normalizedEndpoint, defaultPort);
    }

    private static string BuildCacheKey(ConfigurationOptions options, int databaseIndex, string password, string advancedOptions)
    {
        string rawKey = string.Join("|",
            options.EndPoints.Select(item => item.ToString()).OrderBy(item => item, StringComparer.OrdinalIgnoreCase)) +
            "|" + databaseIndex.ToString(CultureInfo.InvariantCulture) +
            "|" + (options.User ?? string.Empty) +
            "|" + (options.Ssl ? "ssl" : "plain") +
            "|" + (options.ServiceName ?? string.Empty) +
            "|" + (advancedOptions?.Trim() ?? string.Empty) +
            "|" + (options.Password ?? password ?? string.Empty);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hash);
    }
}
