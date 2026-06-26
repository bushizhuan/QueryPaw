using System.Globalization;
using System.Text;
using SqlAnalyzer.Core.Models;
using StackExchange.Redis;

namespace SqlAnalyzer.Data.Redis;

internal static class RedisResultMapper
{
    private const int MaxCellLength = 64 * 1024;

    public static QueryResultSet BuildResultSet(RedisCommandStatement statement, RedisResult result, int previewLimit)
    {
        string command = statement.Command.ToUpperInvariant();
        if (command == "INFO")
        {
            return BuildInfoResultSet(statement, result, previewLimit);
        }

        if (command == "HGETALL")
        {
            return BuildPairResultSet(statement, result, "Field", "Value", previewLimit);
        }

        if (command == "SCAN")
        {
            return BuildScanResultSet(statement, result, previewLimit);
        }

        if (result.Length >= 0)
        {
            return BuildArrayResultSet(statement, result, previewLimit);
        }

        return new QueryResultSet
        {
            Name = BuildResultName(statement),
            Columns = ["Result"],
            DataTypeNames = ["Redis"],
            Rows = [[ FormatRedisResult(result) ]]
        };
    }

    public static QueryResultSet BuildErrorResultSet(RedisCommandStatement statement, string message)
    {
        return new QueryResultSet
        {
            Name = $"Command {statement.StatementIndex} Error",
            Columns = ["Message"],
            Rows = [[ $"Command {statement.StatementIndex} ({statement.Command}) failed: {message}" ]]
        };
    }

    public static QueryResultSet BuildMessageResultSet(string message)
    {
        return new QueryResultSet
        {
            Name = "Message",
            Columns = ["Message"],
            Rows = [[ message ]]
        };
    }

    private static QueryResultSet BuildArrayResultSet(RedisCommandStatement statement, RedisResult result, int previewLimit)
    {
        RedisResult[] items = ToRedisArray(result);
        int takeCount = Math.Min(items.Length, previewLimit);
        object?[][] rows = new object?[takeCount][];
        for (int index = 0; index < takeCount; index++)
        {
            rows[index] =
            [
                (index + 1).ToString(CultureInfo.InvariantCulture),
                FormatRedisResult(items[index])
            ];
        }

        return new QueryResultSet
        {
            Name = BuildResultName(statement),
            Columns = ["Index", "Value"],
            DataTypeNames = ["Integer", "Redis"],
            Rows = rows,
            PreviewLimit = previewLimit,
            IsPreviewTruncated = items.Length > takeCount
        };
    }

    private static QueryResultSet BuildPairResultSet(
        RedisCommandStatement statement,
        RedisResult result,
        string keyColumnName,
        string valueColumnName,
        int previewLimit)
    {
        if (result.Length < 0)
        {
            return BuildResultSet(statement with { Command = "VALUE" }, result, previewLimit);
        }

        RedisResult[] items = ToRedisArray(result);
        int pairCount = items.Length / 2;
        int takeCount = Math.Min(pairCount, previewLimit);
        object?[][] rows = new object?[takeCount][];
        for (int pairIndex = 0; pairIndex < takeCount; pairIndex++)
        {
            rows[pairIndex] =
            [
                FormatRedisResult(items[pairIndex * 2]),
                FormatRedisResult(items[pairIndex * 2 + 1])
            ];
        }

        return new QueryResultSet
        {
            Name = BuildResultName(statement),
            Columns = [keyColumnName, valueColumnName],
            DataTypeNames = ["Redis", "Redis"],
            Rows = rows,
            PreviewLimit = previewLimit,
            IsPreviewTruncated = pairCount > takeCount
        };
    }

    private static QueryResultSet BuildScanResultSet(RedisCommandStatement statement, RedisResult result, int previewLimit)
    {
        if (result.Length != 2)
        {
            return BuildArrayResultSet(statement, result, previewLimit);
        }

        RedisResult cursorResult = result[0];
        RedisResult keysResult = result[1];
        if (keysResult.Length < 0)
        {
            return BuildArrayResultSet(statement, result, previewLimit);
        }

        RedisResult[] keys = ToRedisArray(keysResult);
        int takeCount = Math.Min(keys.Length, previewLimit);
        object?[][] rows = new object?[takeCount][];
        string cursor = FormatRedisResult(cursorResult);
        for (int index = 0; index < takeCount; index++)
        {
            rows[index] =
            [
                cursor,
                FormatRedisResult(keys[index])
            ];
        }

        return new QueryResultSet
        {
            Name = BuildResultName(statement),
            Columns = ["Cursor", "Key"],
            DataTypeNames = ["RedisCursor", "RedisKey"],
            Rows = rows,
            PreviewLimit = previewLimit,
            IsPreviewTruncated = keys.Length > takeCount
        };
    }

    private static QueryResultSet BuildInfoResultSet(RedisCommandStatement statement, RedisResult result, int previewLimit)
    {
        string info = FormatRedisResult(result);
        string section = string.Empty;
        List<object?[]> rows = [];

        foreach (string rawLine in info.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                section = line.TrimStart('#').Trim();
                continue;
            }

            int separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            rows.Add(
            [
                section,
                line[..separatorIndex],
                line[(separatorIndex + 1)..]
            ]);

            if (rows.Count >= previewLimit)
            {
                break;
            }
        }

        return new QueryResultSet
        {
            Name = BuildResultName(statement),
            Columns = ["Section", "Name", "Value"],
            DataTypeNames = ["String", "String", "String"],
            Rows = rows,
            PreviewLimit = previewLimit,
            IsPreviewTruncated = rows.Count >= previewLimit
        };
    }

    private static string BuildResultName(RedisCommandStatement statement)
    {
        return $"Command {statement.StatementIndex}: {statement.Command}";
    }

    private static string FormatRedisResult(RedisResult result)
    {
        if (result.IsNull)
        {
            return "(null)";
        }

        if (result.Length >= 0)
        {
            RedisResult[] items = ToRedisArray(result);
            string text = "[" + string.Join(", ", items.Select(FormatRedisResult)) + "]";
            return TruncateCell(text);
        }

        try
        {
            byte[]? bytes = (byte[]?)result;
            if (bytes == null)
            {
                return TruncateCell(result.ToString() ?? string.Empty);
            }

            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            string text = DecodeBytes(bytes);
            return TruncateCell(text);
        }
        catch
        {
            return TruncateCell(result.ToString() ?? string.Empty);
        }
    }

    private static RedisResult[] ToRedisArray(RedisResult result)
    {
        return (RedisResult[]?)result ?? Array.Empty<RedisResult>();
    }

    private static string DecodeBytes(byte[] bytes)
    {
        try
        {
            string text = Encoding.UTF8.GetString(bytes);
            if (text.Contains('\uFFFD', StringComparison.Ordinal))
            {
                return BuildBinaryPreview(bytes);
            }

            return text;
        }
        catch
        {
            return BuildBinaryPreview(bytes);
        }
    }

    private static string BuildBinaryPreview(byte[] bytes)
    {
        int takeCount = Math.Min(bytes.Length, 64);
        string preview = Convert.ToHexString(bytes.AsSpan(0, takeCount));
        return bytes.Length > takeCount
            ? $"<binary {bytes.Length} bytes; hex {preview}...>"
            : $"<binary {bytes.Length} bytes; hex {preview}>";
    }

    private static string TruncateCell(string value)
    {
        if (value.Length <= MaxCellLength)
        {
            return value;
        }

        return value[..MaxCellLength] + $"... <truncated; {value.Length} chars>";
    }
}
