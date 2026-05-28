using System.Data.Common;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;

namespace SqlAnalyzer.Data.Explorer;

public sealed class ConnectionValidationService : IConnectionValidationService
{
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(10);
    private readonly DbProviderRuntime _runtime;
    public ConnectionValidationService(IDatabaseProviderCatalog providerCatalog)
    {
        _runtime = new DbProviderRuntime(providerCatalog);
    }
    public async Task<string> ValidateConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        DatabaseProviderDefinition provider = _runtime.GetProvider(profile);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return "文档型数据库暂不支持 SQL 连接测试。";
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ValidationTimeout);

        try
        {
            await using DbConnection connection = await OpenConnectionAsync(provider, profile, timeoutSource.Token);
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = provider.TestSql;
            object? scalar = await command.ExecuteScalarAsync(timeoutSource.Token);
            return $"连接成功。验证返回值：{scalar ?? "(null)"}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"连接测试超时，已等待 {ValidationTimeout.TotalSeconds:0} 秒。");
        }
    }
    private async Task<DbConnection> OpenConnectionAsync(DatabaseProviderDefinition provider, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        DbProviderFactory factory = _runtime.ResolveFactory(provider, profile);
        DbConnection connection = factory.CreateConnection() ?? throw new InvalidOperationException($"Cannot create connection for provider '{provider.Name}'.");
        connection.ConnectionString = _runtime.BuildConnectionString(provider, profile);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
