using System.Diagnostics;
using SqlAnalyzer.Core.Models;
using StackExchange.Redis;

namespace SqlAnalyzer.Data.Redis;

internal sealed class RedisExecutionService
{
    private const int DefaultPreviewRows = 500;
    private const int MaxPreviewRows = 10000;
    private readonly RedisConnectionManager _connectionManager;

    public RedisExecutionService(RedisConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public async Task<QueryExecutionResult> ExecuteAsync(QueryExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Connection == null)
        {
            return BuildMessageResult("Editor mode: no Redis connection selected.");
        }

        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return BuildMessageResult("No Redis command supplied.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<RedisCommandStatement> statements;
        try
        {
            statements = RedisCommandParser.Parse(request.Sql);
        }
        catch (Exception ex)
        {
            return BuildMessageResult("Redis command parse failed: " + ex.Message, stopwatch.Elapsed);
        }

        if (statements.Count == 0)
        {
            return BuildMessageResult("No Redis command supplied.", stopwatch.Elapsed);
        }

        int previewLimit = Math.Clamp(request.MaxPreviewRows <= 0 ? DefaultPreviewRows : request.MaxPreviewRows, 1, MaxPreviewRows);
        List<QueryResultSet> resultSets = [];
        bool previewTruncated = false;

        try
        {
            RedisDatabaseHandle handle = await _connectionManager.GetDatabaseAsync(request.Connection, cancellationToken);
            foreach (RedisCommandStatement statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateCommand(statement);

                object[] arguments = statement.Arguments.Cast<object>().ToArray();
                RedisResult result = await handle.Database
                    .ExecuteAsync(statement.Command, arguments)
                    .WaitAsync(cancellationToken);

                QueryResultSet resultSet = RedisResultMapper.BuildResultSet(statement, result, previewLimit);
                resultSets.Add(resultSet);
                previewTruncated |= resultSet.IsPreviewTruncated;
            }

            stopwatch.Stop();
            return new QueryExecutionResult
            {
                Summary = previewTruncated
                    ? $"Completed {statements.Count} Redis command(s), preview limited to {previewLimit} row(s)."
                    : $"Completed {statements.Count} Redis command(s).",
                Duration = stopwatch.Elapsed,
                ResultSets = resultSets,
                IsPreviewTruncated = previewTruncated
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            RedisCommandStatement failedStatement = statements[Math.Min(resultSets.Count, statements.Count - 1)];
            string errorMessage = BuildFriendlyRedisErrorMessage(ex);
            resultSets.Add(RedisResultMapper.BuildErrorResultSet(failedStatement, errorMessage));
            return new QueryExecutionResult
            {
                Summary = $"Redis command failed at statement {failedStatement.StatementIndex}: {errorMessage}",
                Duration = stopwatch.Elapsed,
                ResultSets = resultSets,
                Error = new QueryExecutionErrorInfo
                {
                    Message = errorMessage,
                    StatementIndex = failedStatement.StatementIndex,
                    StatementStartOffset = request.SqlBaseOffset + failedStatement.StartOffset,
                    RelativeLine = failedStatement.StartLine,
                    RelativeColumn = failedStatement.StartColumn,
                    AbsoluteLine = failedStatement.StartLine,
                    AbsoluteColumn = failedStatement.StartColumn,
                    AbsoluteOffset = request.SqlBaseOffset + failedStatement.StartOffset,
                    IsPositionInferred = false
                }
            };
        }
    }

    public async Task<string> ValidateConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            RedisDatabaseHandle handle = await _connectionManager.GetDatabaseAsync(profile, cancellationToken);
            RedisResult result = await handle.Database.ExecuteAsync("PING").WaitAsync(cancellationToken);
            return $"Redis 连接成功。DB：{handle.DatabaseIndex}，验证返回值：{result}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(BuildFriendlyRedisErrorMessage(ex), ex);
        }
    }

    private static void ValidateCommand(RedisCommandStatement statement)
    {
        string command = statement.Command.ToUpperInvariant();
        string firstArgument = statement.Arguments.Count > 0 ? statement.Arguments[0].ToUpperInvariant() : string.Empty;

        if (command is "KEYS")
        {
            throw new InvalidOperationException("KEYS can block Redis on large keyspaces. Please use SCAN instead.");
        }

        if (command is "FLUSHALL" or "FLUSHDB" or "SHUTDOWN" or "MONITOR" or "DEBUG" or "EVAL" or "EVALSHA")
        {
            throw new InvalidOperationException($"Redis command '{command}' is blocked in QueryPaw phase 1 for safety.");
        }

        if (command == "SELECT")
        {
            throw new InvalidOperationException("Redis command 'SELECT' is blocked. Configure DB Index in the connection profile instead.");
        }

        if (command == "CONFIG" && firstArgument is "SET" or "REWRITE" or "RESETSTAT")
        {
            throw new InvalidOperationException($"Redis command '{command} {firstArgument}' is blocked in QueryPaw phase 1 for safety.");
        }

        if (command == "ACL" && firstArgument is "SETUSER" or "DELUSER" or "LOAD" or "SAVE")
        {
            throw new InvalidOperationException($"Redis command '{command} {firstArgument}' is blocked in QueryPaw phase 1 for safety.");
        }

        if (command == "SCRIPT" && firstArgument == "KILL")
        {
            throw new InvalidOperationException("Redis command 'SCRIPT KILL' is blocked in QueryPaw phase 1 for safety.");
        }

        if (command == "CLIENT" && firstArgument == "KILL")
        {
            throw new InvalidOperationException("Redis command 'CLIENT KILL' is blocked in QueryPaw phase 1 for safety.");
        }

        if (command == "CLUSTER" && firstArgument is "RESET" or "FAILOVER" or "FORGET" or "MEET" or "REPLICATE" or "SET-CONFIG-EPOCH" or "SETSLOT")
        {
            throw new InvalidOperationException($"Redis command '{command} {firstArgument}' is blocked in QueryPaw phase 1 for safety.");
        }

        if (command is "SUBSCRIBE" or "PSUBSCRIBE" or "SSUBSCRIBE")
        {
            throw new InvalidOperationException($"Redis command '{command}' is a blocking subscription command and is not supported in QueryPaw phase 1.");
        }

        if (command == "XREAD" && statement.Arguments.Any(argument => string.Equals(argument, "BLOCK", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Redis command 'XREAD BLOCK' is not supported in QueryPaw phase 1.");
        }
    }

    private static QueryExecutionResult BuildMessageResult(string message, TimeSpan? duration = null)
    {
        return new QueryExecutionResult
        {
            Summary = message,
            Duration = duration ?? TimeSpan.Zero,
            ResultSets = [RedisResultMapper.BuildMessageResultSet(message)]
        };
    }

    private static string BuildFriendlyRedisErrorMessage(Exception ex)
    {
        string message = ex.Message;
        if (message.Contains("AuthenticationFailure", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("WRONGPASS", StringComparison.OrdinalIgnoreCase))
        {
            return "Redis 认证失败：请确认密码是否正确。本地 Redis 3.x 或 requirepass 配置通常不需要用户名，请将用户名留空，只填写密码。";
        }

        if (message.Contains("No connection is active", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no connection became available", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
        {
            return "Redis 连接失败：请确认主机、端口是否正确，Redis 服务是否正在监听，密码和 SSL/高级参数是否匹配。";
        }

        return message;
    }
}
