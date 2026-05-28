using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.Data.Formatting;

public sealed class PassthroughSqlFormatterService : ISqlFormatterService
{
    private const int SafeSqlFormattingLength = 20000;
    private static readonly string[] ClauseKeywords =
    [
        "SELECT", "FROM", "WHERE", "GROUP BY", "ORDER BY", "HAVING",
        "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "FULL JOIN", "JOIN",
        "UNION ALL", "UNION", "INSERT INTO", "VALUES", "UPDATE", "SET",
        "DELETE FROM", "DELETE", "EXPLAIN", "ON"
    ];
    public string Format(string text)
    {
        string normalized = NormalizeInput(text);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return DetectContentType(normalized) switch
        {
            FormatContentType.Json => FormatJson(normalized),
            FormatContentType.Xml => FormatXml(normalized),
            FormatContentType.Unknown => normalized,
            _ => FormatSql(normalized)
        };
    }
    private static FormatContentType DetectContentType(string text)
    {
        if (LooksLikeJson(text) && IsValidJson(text))
        {
            return FormatContentType.Json;
        }

        if (LooksLikeJson(text))
        {
            return FormatContentType.Unknown;
        }

        if (LooksLikeXml(text) && IsValidXml(text))
        {
            return FormatContentType.Xml;
        }

        if (LooksLikeXml(text))
        {
            return FormatContentType.Unknown;
        }

        return FormatContentType.Sql;
    }
    private static string NormalizeInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\uFEFF');
        string[] lines = normalized.Split('\n');
        int start = 0;
        int end = lines.Length - 1;

        while (start <= end && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, lines.Skip(start).Take(end - start + 1)).Trim();
    }
    private static bool LooksLikeJson(string text)
    {
        return (text.StartsWith("{") && text.EndsWith("}")) || (text.StartsWith("[") && text.EndsWith("]"));
    }
    private static bool LooksLikeXml(string text)
    {
        return text.StartsWith("<") && text.EndsWith(">");
    }
    private static bool IsValidJson(string text)
    {
        try
        {
            JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static bool IsValidXml(string text)
    {
        try
        {
            XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static string FormatXml(string text)
    {
        XDocument document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        StringBuilder builder = new();
        XmlWriterSettings settings = new()
        {
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace
        };

        using XmlWriter writer = XmlWriter.Create(builder, settings);
        document.Save(writer);
        return builder.ToString().Trim();
    }
    private static string FormatJson(string text)
    {
        JsonElement value = JsonSerializer.Deserialize<JsonElement>(text);
        StringBuilder builder = new();
        WriteJsonValue(builder, value, 0);
        return builder.ToString();
    }
    private static void WriteJsonValue(StringBuilder builder, JsonElement value, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteJsonObject(builder, value, depth);
                break;
            case JsonValueKind.Array:
                WriteJsonArray(builder, value, depth);
                break;
            case JsonValueKind.String:
                builder.Append('"').Append(EscapeJsonString(value.GetString() ?? string.Empty)).Append('"');
                break;
            case JsonValueKind.Number:
                builder.Append(value.GetRawText());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append(value.GetBoolean().ToString().ToLowerInvariant());
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                builder.Append("null");
                break;
        }
    }
    private static void WriteJsonObject(StringBuilder builder, JsonElement value, int depth)
    {
        JsonProperty[] properties = value.EnumerateObject().ToArray();
        builder.Append("{");
        if (properties.Length == 0)
        {
            builder.Append("}");
            return;
        }

        builder.AppendLine();
        for (int i = 0; i < properties.Length; i++)
        {
            JsonProperty property = properties[i];
            builder.Append(new string(' ', (depth + 1) * 2));
            builder.Append('"').Append(EscapeJsonString(property.Name)).Append("\": ");
            WriteJsonValue(builder, property.Value, depth + 1);
            if (i < properties.Length - 1)
            {
                builder.Append(",");
            }

            builder.AppendLine();
        }

        builder.Append(new string(' ', depth * 2)).Append("}");
    }
    private static void WriteJsonArray(StringBuilder builder, JsonElement value, int depth)
    {
        JsonElement[] items = value.EnumerateArray().ToArray();
        builder.Append("[");
        if (items.Length == 0)
        {
            builder.Append("]");
            return;
        }

        builder.AppendLine();
        for (int i = 0; i < items.Length; i++)
        {
            builder.Append(new string(' ', (depth + 1) * 2));
            WriteJsonValue(builder, items[i], depth + 1);
            if (i < items.Length - 1)
            {
                builder.Append(",");
            }

            builder.AppendLine();
        }

        builder.Append(new string(' ', depth * 2)).Append("]");
    }
    private static string FormatSql(string sql)
    {
        if (sql.Length > SafeSqlFormattingLength)
        {
            return sql;
        }

        List<string> protectedSegments = [];
        string protectedSql = ProtectSqlSegments(sql, protectedSegments);
        string[] statements = SplitSqlStatements(protectedSql);
        string separator = Environment.NewLine + Environment.NewLine;
        string formatted = string.Join(
            separator,
            statements
                .Select(FormatSingleSqlStatement)
                .Where(item => !string.IsNullOrWhiteSpace(item)));
        return RestoreProtectedSegments(formatted, protectedSegments).Trim();
    }
    private static string FormatSingleSqlStatement(string sql)
    {
        string normalized = Regex.Replace(sql, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = NormalizeKeywords(normalized);
        normalized = Regex.Replace(normalized, @"\(\s*(SELECT|WITH)\b", "(" + Environment.NewLine + "$1", RegexOptions.IgnoreCase);

        foreach (string clause in ClauseKeywords
                     .Where(item => item is not "ON" and not "JOIN")
                     .OrderByDescending(item => item.Length))
        {
            normalized = Regex.Replace(
                normalized,
                @"\s*\b" + Regex.Escape(clause).Replace("\\ ", "\\s+") + @"\b\s*",
                Environment.NewLine + clause + Environment.NewLine + "    ",
                RegexOptions.IgnoreCase);
        }

        string[] joinClauses =
        [
            "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "FULL OUTER JOIN",
            "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "INNER JOIN", "CROSS JOIN", "JOIN"
        ];

        foreach (string joinClause in joinClauses)
        {
            normalized = Regex.Replace(
                normalized,
                @"\s*\b" + Regex.Escape(joinClause).Replace("\\ ", "\\s+") + @"\b\s*",
                Environment.NewLine + "    " + joinClause + " ",
                RegexOptions.IgnoreCase);
        }

        normalized = Regex.Replace(normalized, @"\s+\bON\b\s+", Environment.NewLine + "        ON ", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+\bAND\b\s+", Environment.NewLine + "        AND ", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+\bOR\b\s+", Environment.NewLine + "        OR ", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+\bWHEN\b\s+", Environment.NewLine + "        WHEN ", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+\bELSE\b\s+", Environment.NewLine + "        ELSE ", RegexOptions.IgnoreCase);
        normalized = BreakTopLevelCommas(normalized);
        normalized = IndentByParentheses(normalized);

        string[] lines = normalized
            .Split([Environment.NewLine], StringSplitOptions.None)
            .Select(item => item.TrimEnd())
            .Where((item, index) => item.Length > 0 || (index > 0 && index < normalized.Length - 1))
            .ToArray();

        return string.Join(Environment.NewLine, lines).Trim();
    }
    private static string ProtectSqlSegments(string sql, List<string> protectedSegments)
    {
        // 注释和字符串先占位，后面的换行规则就不会误伤字面量。
        StringBuilder builder = new();
        for (int index = 0; index < sql.Length;)
        {
            char current = sql[index];
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                int start = index;
                index += 2;
                while (index < sql.Length && sql[index] != '\n')
                {
                    index++;
                }

                builder.Append(AddProtectedSegment(sql[start..index], protectedSegments));
                continue;
            }

            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                int start = index;
                index += 2;
                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/'))
                {
                    index++;
                }

                index = Math.Min(index + 2, sql.Length);
                builder.Append(AddProtectedSegment(sql[start..index], protectedSegments));
                continue;
            }

            if (current is '\'' or '"' or '`')
            {
                builder.Append(AddProtectedSegment(ReadQuoted(sql, ref index, current), protectedSegments));
                continue;
            }

            if (current == '[')
            {
                builder.Append(AddProtectedSegment(ReadBracketQuoted(sql, ref index), protectedSegments));
                continue;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString();
    }
    private static string[] SplitSqlStatements(string sql)
    {
        List<string> statements = [];
        StringBuilder builder = new();
        foreach (char current in sql)
        {
            if (current == ';')
            {
                string statement = builder.ToString().Trim();
                if (statement.Length > 0)
                {
                    statements.Add(statement + ";");
                }

                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        string trailing = builder.ToString().Trim();
        if (trailing.Length > 0)
        {
            statements.Add(trailing);
        }

        return statements.ToArray();
    }
    private static string BreakTopLevelCommas(string sql)
    {
        StringBuilder builder = new();
        int depth = 0;
        foreach (char current in sql)
        {
            if (current == '(')
            {
                depth++;
                builder.Append(current);
                continue;
            }

            if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
                builder.Append(current);
                continue;
            }

            if (current == ',' && depth == 0)
            {
                builder.Append(',').Append(Environment.NewLine).Append("    ");
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
    private static string IndentByParentheses(string sql)
    {
        StringBuilder builder = new();
        int indent = 0;
        string[] lines = sql.Split([Environment.NewLine], StringSplitOptions.None);
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(")", StringComparison.Ordinal))
            {
                indent = Math.Max(0, indent - 1);
            }

            int baseIndent = rawLine.TakeWhile(char.IsWhiteSpace).Count() / 4;
            builder.Append(new string(' ', Math.Max(0, baseIndent + indent) * 4));
            builder.AppendLine(line);

            indent += line.Count(ch => ch == '(');
            indent -= line.Count(ch => ch == ')');
            indent = Math.Max(0, indent);
        }

        return builder.ToString().TrimEnd();
    }
    private static string RestoreProtectedSegments(string sql, IReadOnlyList<string> protectedSegments)
    {
        string restored = sql;
        for (int index = 0; index < protectedSegments.Count; index++)
        {
            restored = restored.Replace(BuildProtectedToken(index), protectedSegments[index], StringComparison.Ordinal);
        }

        return restored;
    }
    private static string AddProtectedSegment(string text, List<string> protectedSegments)
    {
        int index = protectedSegments.Count;
        protectedSegments.Add(text);
        return BuildProtectedToken(index);
    }
    private static string BuildProtectedToken(int index)
    {
        return $"__SQLFMT_{index}__";
    }
    private static string ReadQuoted(string sql, ref int index, char quote)
    {
        StringBuilder builder = new();
        builder.Append(quote);
        index++;
        while (index < sql.Length)
        {
            char current = sql[index];
            builder.Append(current);
            index++;

            if (current == quote)
            {
                if (quote == '\'' && index < sql.Length && sql[index] == quote)
                {
                    builder.Append(sql[index]);
                    index++;
                    continue;
                }

                break;
            }
        }

        return builder.ToString();
    }
    private static string ReadBracketQuoted(string sql, ref int index)
    {
        StringBuilder builder = new();
        builder.Append('[');
        index++;
        while (index < sql.Length)
        {
            char current = sql[index];
            builder.Append(current);
            index++;
            if (current == ']')
            {
                break;
            }
        }

        return builder.ToString();
    }
    private static string NormalizeKeywords(string line)
    {
        string result = line;
        foreach (string keyword in ClauseKeywords.OrderByDescending(item => item.Length))
        {
            result = Regex.Replace(
                result,
                @"\b" + Regex.Escape(keyword).Replace("\\ ", "\\s+") + @"\b",
                keyword,
                RegexOptions.IgnoreCase);
        }

        string[] inlineKeywords =
        [
            "AND", "OR", "WHEN", "ELSE", "THEN", "CASE", "END", "AS",
            "IN", "IS", "NULL", "NOT", "LIKE", "BETWEEN", "EXISTS",
            "DISTINCT", "ASC", "DESC", "ON"
        ];
        foreach (string keyword in inlineKeywords)
        {
            result = Regex.Replace(result, @"\b" + keyword + @"\b", keyword, RegexOptions.IgnoreCase);
        }

        return result;
    }
    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    private enum FormatContentType
    {
        Sql,
        Json,
        Xml,
        Unknown
    }
}
