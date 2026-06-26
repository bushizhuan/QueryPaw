using System.Collections.Concurrent;
using SqlAnalyzer.Core.Models;
using StackExchange.Redis;

namespace SqlAnalyzer.Data.Redis;

internal sealed record RedisDatabaseHandle(
    IDatabase Database,
    int DatabaseIndex,
    string EndpointDisplay);

internal sealed class RedisConnectionManager
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<ConnectionMultiplexer>>> Connections = new(StringComparer.Ordinal);
    private readonly RedisConnectionOptionsBuilder _optionsBuilder = new();

    public async Task<RedisDatabaseHandle> GetDatabaseAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        RedisConnectionSettings settings = _optionsBuilder.Build(profile);
        Lazy<Task<ConnectionMultiplexer>> lazyConnection = Connections.GetOrAdd(
            settings.CacheKey,
            _ => new Lazy<Task<ConnectionMultiplexer>>(
                () => ConnectionMultiplexer.ConnectAsync(settings.Options),
                LazyThreadSafetyMode.ExecutionAndPublication));

        ConnectionMultiplexer connection;
        try
        {
            connection = await lazyConnection.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            Connections.TryRemove(settings.CacheKey, out _);
            throw;
        }

        if (!connection.IsConnected)
        {
            Connections.TryRemove(settings.CacheKey, out _);
            TryClose(connection);
            lazyConnection = Connections.GetOrAdd(
                settings.CacheKey,
                _ => new Lazy<Task<ConnectionMultiplexer>>(
                    () => ConnectionMultiplexer.ConnectAsync(settings.Options),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            connection = await lazyConnection.Value.WaitAsync(cancellationToken);
        }

        return new RedisDatabaseHandle(
            connection.GetDatabase(settings.DatabaseIndex),
            settings.DatabaseIndex,
            settings.EndpointDisplay);
    }

    public static void ClearAll()
    {
        foreach (Lazy<Task<ConnectionMultiplexer>> lazyConnection in Connections.Values)
        {
            if (!lazyConnection.IsValueCreated)
            {
                continue;
            }

            if (lazyConnection.Value.IsCompletedSuccessfully)
            {
                TryClose(lazyConnection.Value.Result);
            }
        }

        Connections.Clear();
    }

    private static void TryClose(ConnectionMultiplexer connection)
    {
        try
        {
            connection.Close(allowCommandsToComplete: false);
        }
        catch
        {
        }

        try
        {
            connection.Dispose();
        }
        catch
        {
        }
    }
}
