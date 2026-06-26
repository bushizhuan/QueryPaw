using System.Text;

namespace SqlAnalyzer.Data.Redis;

internal sealed record RedisCommandStatement(
    string RawText,
    string Command,
    IReadOnlyList<string> Arguments,
    int StatementIndex,
    int StartOffset,
    int StartLine,
    int StartColumn);

internal static class RedisCommandParser
{
    public static IReadOnlyList<RedisCommandStatement> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<RedisCommandStatement>();
        }

        List<RedisCommandStatement> statements = [];
        StringBuilder builder = new();
        bool inQuote = false;
        char quoteChar = '\0';
        bool escaped = false;
        int statementStartOffset = 0;
        int statementStartLine = 1;
        int statementStartColumn = 1;
        int line = 1;
        int column = 1;

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            bool isTerminator = !inQuote && (current == ';' || current == '\r' || current == '\n');
            if (isTerminator)
            {
                AddStatement(statements, builder.ToString(), statementStartOffset, statementStartLine, statementStartColumn);
                builder.Clear();

                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                line++;
                column = 1;
                statementStartOffset = index + 1;
                statementStartLine = line;
                statementStartColumn = column;
                continue;
            }

            builder.Append(current);

            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\' && inQuote)
            {
                escaped = true;
            }
            else if ((current == '"' || current == '\'') && (!inQuote || current == quoteChar))
            {
                inQuote = !inQuote;
                quoteChar = inQuote ? current : '\0';
            }

            column++;
        }

        AddStatement(statements, builder.ToString(), statementStartOffset, statementStartLine, statementStartColumn);
        return statements;
    }

    private static void AddStatement(
        List<RedisCommandStatement> statements,
        string rawText,
        int startOffset,
        int startLine,
        int startColumn)
    {
        string trimmed = rawText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
        {
            return;
        }

        IReadOnlyList<string> tokens = Tokenize(trimmed);
        if (tokens.Count == 0)
        {
            return;
        }

        statements.Add(new RedisCommandStatement(
            trimmed,
            tokens[0].ToUpperInvariant(),
            tokens.Skip(1).ToArray(),
            statements.Count + 1,
            startOffset,
            startLine,
            startColumn));
    }

    private static IReadOnlyList<string> Tokenize(string commandText)
    {
        List<string> tokens = [];
        StringBuilder tokenBuilder = new();
        bool inQuote = false;
        char quoteChar = '\0';
        bool escaped = false;

        foreach (char current in commandText)
        {
            if (escaped)
            {
                tokenBuilder.Append(current);
                escaped = false;
                continue;
            }

            if (current == '\\' && inQuote)
            {
                escaped = true;
                continue;
            }

            if ((current == '"' || current == '\'') && (!inQuote || current == quoteChar))
            {
                inQuote = !inQuote;
                quoteChar = inQuote ? current : '\0';
                continue;
            }

            if (!inQuote && char.IsWhiteSpace(current))
            {
                FlushToken(tokens, tokenBuilder);
                continue;
            }

            tokenBuilder.Append(current);
        }

        if (inQuote)
        {
            throw new InvalidOperationException("Redis command contains an unterminated quoted string.");
        }

        FlushToken(tokens, tokenBuilder);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder tokenBuilder)
    {
        if (tokenBuilder.Length == 0)
        {
            return;
        }

        tokens.Add(tokenBuilder.ToString());
        tokenBuilder.Clear();
    }
}
