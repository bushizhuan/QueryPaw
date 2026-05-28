using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Input;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class CompletionController
{
    // Completion only needs the statement around the caret; scanning a whole script makes typing feel heavy.
    private const int MaxCompletionContextLength = 32000;

    public enum PopupKeyAction
    {
        None,
        MoveUp,
        MoveDown,
        Commit,
        Close
    }

    public sealed record CompletionRelationReference(string? SchemaName, string TableName, string? Alias)
    {
        public string EffectiveName => string.IsNullOrWhiteSpace(Alias) ? TableName : Alias;
    }

    public sealed record CompletionContextInfo(
        string Kind,
        string Prefix,
        string? ResolvedObjectName = null,
        string? SingleRelationName = null,
        IReadOnlyList<CompletionRelationReference>? Relations = null,
        string? Qualifier = null)
    {
        public bool AllowEmptyPrefix => string.Equals(Kind, "member-column", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<CompletionRelationReference> RelationReferences => Relations ?? Array.Empty<CompletionRelationReference>();

        public bool IsMemberAccess => string.Equals(Kind, "member-column", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record CompletionRefreshRequest(
        CompletionContextInfo ContextInfo,
        ConnectionProfile? Connection,
        string Schema,
        int Sequence,
        CancellationToken Token)
    {
        public string Prefix => ContextInfo.Prefix;

        public string Context => ContextInfo.Kind;

        public string? ResolvedObjectName => ContextInfo.ResolvedObjectName;

        public string? SingleRelationName => ContextInfo.SingleRelationName;

        public IReadOnlyList<CompletionRelationReference> RelationReferences => ContextInfo.RelationReferences;

        public string? Qualifier => ContextInfo.Qualifier;

        public bool AllowEmptyPrefix => ContextInfo.AllowEmptyPrefix;
    }

    private CancellationTokenSource? _debounceTokenSource;
    private int _refreshSequence;
    public bool ShouldHideOnKeyUp(Key key)
    {
        return key is Key.Space or Key.Delete or Key.Back;
    }
    public PopupKeyAction GetPopupKeyAction(Key key)
    {
        return key switch
        {
            Key.Down => PopupKeyAction.MoveDown,
            Key.Up => PopupKeyAction.MoveUp,
            Key.Enter => PopupKeyAction.Commit,
            Key.Tab => PopupKeyAction.Commit,
            Key.Escape => PopupKeyAction.Close,
            _ => PopupKeyAction.None
        };
    }
    public bool IsIdentifierChar(char value)
    {
        return IsIdentifierPart(value);
    }
    public string GetPrefix(string text, int caret)
    {
        if (caret <= 0 || caret > text.Length)
        {
            return string.Empty;
        }

        (int start, int length) = GetReplacementRange(text, caret);
        return length > 0 ? text.Substring(start, length).TrimEnd() : string.Empty;
    }
    public string GetContext(string text, int caret)
    {
        return GetContextInfo(text, caret).Kind;
    }
    public CompletionContextInfo GetContextInfo(string text, int caret)
    {
        if (caret < 0 || caret > text.Length)
        {
            return new CompletionContextInfo("generic", string.Empty);
        }

        (int _, string statementText, int statementCaret) = GetCurrentStatement(text, caret);
        (int start, int length) = GetReplacementRange(statementText, statementCaret);
        string prefix = length > 0 ? statementText.Substring(start, length).TrimEnd() : string.Empty;
        string beforePrefix = statementText[..start].TrimEnd();
        if (beforePrefix.Length == 0)
        {
            return new CompletionContextInfo("generic", prefix);
        }

        // FROM/JOIN aliases decide which columns are useful, so keep this tied to the current statement.
        List<CompletionRelationReference> relations = ParseRelationReferences(statementText);
        string? singleRelationName = relations.Count == 1 ? relations[0].TableName : null;

        string? qualifier = TryGetQualifier(statementText, start);
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            string? resolvedObjectName = ResolveQualifierObjectName(relations, qualifier);
            return new CompletionContextInfo("member-column", prefix, resolvedObjectName ?? qualifier, singleRelationName, relations, qualifier);
        }

        string lowered = beforePrefix.ToLowerInvariant();
        if (lowered.EndsWith("from") ||
            lowered.EndsWith("join") ||
            lowered.EndsWith("update") ||
            lowered.EndsWith("into") ||
            lowered.EndsWith("table"))
        {
            return new CompletionContextInfo("relation", prefix, null, singleRelationName, relations);
        }

        if (lowered.EndsWith("where") ||
            lowered.EndsWith("and") ||
            lowered.EndsWith("or") ||
            lowered.EndsWith("on") ||
            lowered.EndsWith("set"))
        {
            return new CompletionContextInfo("column", prefix, singleRelationName, singleRelationName, relations);
        }

        if (lowered.EndsWith(",") && IsSelectProjectionContext(statementText, start) && relations.Count > 0)
        {
            return new CompletionContextInfo("select-column", prefix, singleRelationName, singleRelationName, relations);
        }

        if (IsSelectProjectionContext(statementText, start) && relations.Count > 0)
        {
            return new CompletionContextInfo("select-column", prefix, singleRelationName, singleRelationName, relations);
        }

        return new CompletionContextInfo("generic", prefix, null, singleRelationName, relations);
    }
    public int GetDebounceDelayMilliseconds(string prefix)
    {
        return prefix.Length <= 2 ? 45 : 80;
    }
    public CompletionRefreshRequest BeginRefresh(
        string text,
        int caret,
        ConnectionProfile? connection,
        string schema)
    {
        _debounceTokenSource?.Cancel();
        _debounceTokenSource?.Dispose();
        _debounceTokenSource = new CancellationTokenSource();

        CompletionContextInfo contextInfo = GetContextInfo(text, caret);
        int sequence = Interlocked.Increment(ref _refreshSequence);
        return new CompletionRefreshRequest(contextInfo, connection, schema, sequence, _debounceTokenSource.Token);
    }
    public void CancelRefresh()
    {
        Interlocked.Increment(ref _refreshSequence);
        _debounceTokenSource?.Cancel();
        _debounceTokenSource?.Dispose();
        _debounceTokenSource = null;
    }

    public bool IsCurrentRefresh(CancellationToken token, int sequence)
    {
        // A delayed refresh may complete after the next keypress. The UI checks this before touching the popup.
        return !token.IsCancellationRequested && sequence == _refreshSequence;
    }

    public (int Start, int Length) GetReplacementRange(string text, int caret)
    {
        if (caret < 0)
        {
            caret = 0;
        }
        else if (caret > text.Length)
        {
            caret = text.Length;
        }

        int start = caret;
        while (start > 0 && IsIdentifierPart(text[start - 1]))
        {
            start--;
        }

        if (caret > start)
        {
            return (start, caret - start);
        }

        (int localizedStart, int localizedLength) = GetLocalizedTokenBeforeTrailingWhitespace(text, caret);
        if (localizedLength > 0)
        {
            return (localizedStart, localizedLength);
        }

        return (start, caret - start);
    }
    public CompletionItem? MoveSelection(IReadOnlyList<CompletionItem> items, CompletionItem? currentItem, int offset)
    {
        if (items.Count == 0)
        {
            return null;
        }

        int index = 0;
        if (currentItem != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], currentItem))
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
        {
            index = 0;
        }

        index = (index + offset + items.Count) % items.Count;
        return items[index];
    }
    public (string UpdatedText, int CaretOffset) ApplyCompletion(string text, int caret, CompletionItem item)
    {
        (int start, int length) = GetReplacementRange(text, caret);
        string insertText = string.IsNullOrWhiteSpace(item.InsertText) ? item.Text : item.InsertText;
        string updated = text.Remove(start, length).Insert(start, insertText + " ");
        return (updated, start + insertText.Length + 1);
    }
    public Thickness CalculatePopupMargin(Point popupOrigin, Rect parentBounds)
    {
        double maxLeft = Math.Max(parentBounds.Width - 340, 16);
        double maxTop = Math.Max(parentBounds.Height - 260, 16);
        double left = Math.Clamp(popupOrigin.X + 8, 12, maxLeft);
        double top = Math.Clamp(popupOrigin.Y + 6, 12, maxTop);
        return new Thickness(left, top, 0, 0);
    }
    public Thickness CalculateFallbackPopupMargin(string text, int caret, Rect editorBounds)
    {
        if (caret < 0)
        {
            caret = 0;
        }
        else if (caret > text.Length)
        {
            caret = text.Length;
        }

        int lineStart = text.LastIndexOf('\n', Math.Max(caret - 1, 0));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int column = Math.Max(caret - lineStart, 0);
        int line = 0;
        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        double charWidth = 8.2;
        double lineHeight = 22;
        double left = 18 + (column * charWidth);
        double top = 18 + ((line + 1) * lineHeight);
        double maxLeft = Math.Max((editorBounds.Width <= 0 ? 900 : editorBounds.Width) - 340, 16);
        double maxTop = Math.Max((editorBounds.Height <= 0 ? 400 : editorBounds.Height) - 260, 16);
        left = Math.Clamp(left, 16, maxLeft);
        top = Math.Clamp(top, 16, maxTop);
        return new Thickness(left, top, 0, 0);
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_' || value > 127;
    }

    private static (int Start, int Length) GetLocalizedTokenBeforeTrailingWhitespace(string text, int caret)
    {
        int whitespaceStart = caret;
        while (whitespaceStart > 0 && IsCompletionTrailingWhitespace(text[whitespaceStart - 1]))
        {
            whitespaceStart--;
        }

        if (whitespaceStart == caret)
        {
            return (caret, 0);
        }

        int tokenStart = whitespaceStart;
        while (tokenStart > 0 && IsIdentifierPart(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        if (tokenStart == whitespaceStart)
        {
            return (caret, 0);
        }

        string token = text[tokenStart..whitespaceStart];
        return token.Any(static ch => ch > 127)
            ? (tokenStart, caret - tokenStart)
            : (caret, 0);
    }

    private static bool IsCompletionTrailingWhitespace(char value)
    {
        return value == ' ' || value == '\t' || value == '\u3000';
    }

    private static (int Start, string Text, int Caret) GetCurrentStatement(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, string.Empty, 0);
        }

        caret = Math.Clamp(caret, 0, text.Length);

        // Keep enough nearby SQL for aliases and joins, without letting a long scratch file dominate completion.
        int backwardLimit = Math.Max(0, caret - MaxCompletionContextLength);
        int previousSemicolon = -1;
        if (caret > backwardLimit)
        {
            previousSemicolon = text.LastIndexOf(';', caret - 1, caret - backwardLimit);
        }

        int segmentStart = previousSemicolon < 0 ? backwardLimit : previousSemicolon + 1;
        int forwardLimit = Math.Min(text.Length, segmentStart + MaxCompletionContextLength);
        int nextSemicolon = caret < forwardLimit
            ? text.IndexOf(';', caret, forwardLimit - caret)
            : -1;
        int segmentEnd = nextSemicolon < 0 ? forwardLimit : nextSemicolon;
        int segmentLength = Math.Max(0, segmentEnd - segmentStart);
        if (segmentLength == 0)
        {
            return (segmentStart, string.Empty, 0);
        }

        string segmentText = text.Substring(segmentStart, segmentLength);
        string sanitized = SanitizeSql(segmentText);
        int localCaret = Math.Clamp(caret - segmentStart, 0, segmentText.Length);
        int localStart = FindNearestStatementStart(sanitized, 0, localCaret);
        int localEnd = FindNextStatementBoundary(sanitized, localCaret, segmentText.Length);

        if (localEnd < 0)
        {
            localEnd = segmentText.Length;
        }

        localEnd = Math.Clamp(localEnd, localStart, segmentText.Length);
        return (
            segmentStart + localStart,
            segmentText[localStart..localEnd],
            Math.Clamp(localCaret - localStart, 0, localEnd - localStart));
    }

    private static int FindNearestStatementStart(string sanitizedText, int segmentStart, int caret)
    {
        int depth = 0;
        int bestStart = Math.Clamp(segmentStart, 0, sanitizedText.Length);
        int limit = Math.Clamp(caret, 0, sanitizedText.Length);
        for (int index = bestStart; index < limit; index++)
        {
            char current = sanitizedText[index];
            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0 && IsStatementStarterAt(sanitizedText, index) && IsLikelyStandaloneStatementStart(sanitizedText, index, bestStart))
            {
                bestStart = index;
            }
        }

        return bestStart;
    }

    private static int FindNextStatementBoundary(string sanitizedText, int caret, int fallbackEnd)
    {
        int depth = 0;
        int start = Math.Clamp(caret, 0, sanitizedText.Length);
        for (int index = start; index < sanitizedText.Length; index++)
        {
            char current = sanitizedText[index];
            if (current == ';')
            {
                return index;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0 && IsStatementStarterAt(sanitizedText, index) && IsLikelyStandaloneStatementStart(sanitizedText, index, start))
            {
                return index;
            }
        }

        return fallbackEnd;
    }

    private static bool IsStatementStarterAt(string text, int index)
    {
        return IsKeywordAt(text, index, "select") ||
               IsKeywordAt(text, index, "with") ||
               IsKeywordAt(text, index, "update") ||
               IsKeywordAt(text, index, "insert") ||
               IsKeywordAt(text, index, "delete") ||
               IsKeywordAt(text, index, "merge");
    }

    private static bool IsKeywordAt(string text, int index, string keyword)
    {
        if (index < 0 || index + keyword.Length > text.Length)
        {
            return false;
        }

        if (!text.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        char previous = index > 0 ? text[index - 1] : ' ';
        char next = index + keyword.Length < text.Length ? text[index + keyword.Length] : ' ';
        return !IsIdentifierPart(previous) && !IsIdentifierPart(next);
    }

    private static bool IsLikelyStandaloneStatementStart(string text, int index, int fallbackStart)
    {
        int previous = index - 1;
        while (previous >= fallbackStart && (text[previous] == ' ' || text[previous] == '\t'))
        {
            previous--;
        }

        if (previous < fallbackStart)
        {
            return true;
        }

        if (text[previous] == ';')
        {
            return true;
        }

        return text[previous] == '\r' || text[previous] == '\n';
    }

    private static string? TryGetQualifier(string text, int replacementStart)
    {
        if (replacementStart <= 0 || text[replacementStart - 1] != '.')
        {
            return null;
        }

        int end = replacementStart - 1;
        int start = end;
        while (start > 0 && IsIdentifierPart(text[start - 1]))
        {
            start--;
        }

        return end > start ? text[start..end].Trim() : null;
    }

    private static bool IsSelectProjectionContext(string statementText, int caret)
    {
        string sanitized = SanitizeSql(statementText);
        if (!TryFindTopLevelKeyword(sanitized, "select", 0, out int selectIndex) ||
            !TryFindTopLevelKeyword(sanitized, "from", selectIndex + 6, out int fromIndex))
        {
            return false;
        }

        return caret > selectIndex + 6 && caret <= fromIndex;
    }

    private static string? ResolveQualifierObjectName(IEnumerable<CompletionRelationReference> relations, string qualifier)
    {
        CompletionRelationReference? aliasMatch = relations.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.Alias) &&
            string.Equals(item.Alias, qualifier, StringComparison.OrdinalIgnoreCase));
        if (aliasMatch != null)
        {
            return aliasMatch.TableName;
        }

        CompletionRelationReference? tableMatch = relations.FirstOrDefault(item =>
            string.Equals(item.TableName, qualifier, StringComparison.OrdinalIgnoreCase));
        return tableMatch?.TableName;
    }

    private static List<CompletionRelationReference> ParseRelationReferences(string statementText)
    {
        string sanitized = SanitizeSql(statementText);
        List<string> tokens = TokenizeSqlSegment(sanitized);
        List<CompletionRelationReference> relations = [];
        for (int index = 0; index < tokens.Count; index++)
        {
            if (!IsRelationKeyword(tokens[index]))
            {
                continue;
            }

            if (TryParseRelationReference(tokens, ref index, out CompletionRelationReference? relation) && relation != null)
            {
                relations.Add(relation);
            }
        }

        return relations;
    }

    private static bool IsRelationKeyword(string token)
    {
        return token.Equals("from", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("join", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("update", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("into", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseRelationReference(IReadOnlyList<string> tokens, ref int index, out CompletionRelationReference? relation)
    {
        relation = null;
        int current = index + 1;
        if (current >= tokens.Count || tokens[current] == "(")
        {
            return false;
        }

        if (!IsPotentialIdentifier(tokens[current]))
        {
            return false;
        }

        string? schemaName = null;
        string tableName = tokens[current];
        current++;
        if (current + 1 < tokens.Count && tokens[current] == "." && IsPotentialIdentifier(tokens[current + 1]))
        {
            schemaName = tableName;
            tableName = tokens[current + 1];
            current += 2;
        }

        if (current < tokens.Count && tokens[current].Equals("as", StringComparison.OrdinalIgnoreCase))
        {
            current++;
        }

        string? alias = null;
        if (current < tokens.Count && IsPotentialIdentifier(tokens[current]) && !IsTerminatorKeyword(tokens[current]))
        {
            alias = tokens[current];
            current++;
        }

        relation = new CompletionRelationReference(schemaName, tableName, alias);
        index = current - 1;
        return true;
    }

    private static bool IsPotentialIdentifier(string token)
    {
        return !string.IsNullOrWhiteSpace(token) &&
               token != "," &&
               token != "." &&
               token != "(" &&
               token != ")" &&
               token.All(IsIdentifierPart);
    }

    private static bool IsTerminatorKeyword(string token)
    {
        return token.Equals("where", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("group", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("order", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("having", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("join", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("left", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("right", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("inner", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("full", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("cross", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("union", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("set", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("values", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindTopLevelKeyword(string sql, string keyword, int startIndex, out int position)
    {
        position = -1;
        int depth = 0;
        for (int index = Math.Max(0, startIndex); index <= sql.Length - keyword.Length; index++)
        {
            char current = sql[index];
            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth != 0)
            {
                continue;
            }

            if (!sql.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            char previous = index > 0 ? sql[index - 1] : ' ';
            char next = index + keyword.Length < sql.Length ? sql[index + keyword.Length] : ' ';
            if (char.IsLetterOrDigit(previous) || previous == '_')
            {
                continue;
            }

            if (char.IsLetterOrDigit(next) || next == '_')
            {
                continue;
            }

            position = index;
            return true;
        }

        return false;
    }

    private static string SanitizeSql(string sql)
    {
        StringBuilder builder = new StringBuilder(sql.Length);
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inBracket = false;
        bool inBacktick = false;
        bool inLineComment = false;
        bool inBlockComment = false;
        for (int index = 0; index < sql.Length; index++)
        {
            char current = sql[index];
            char next = index + 1 < sql.Length ? sql[index + 1] : '\0';
            if (inLineComment)
            {
                builder.Append(current == '\r' || current == '\n' ? current : ' ');
                if (current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                builder.Append(current == '\r' || current == '\n' ? current : ' ');
                if (current == '*' && next == '/')
                {
                    builder.Append(' ');
                    index++;
                    inBlockComment = false;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBracket && !inBacktick)
            {
                if (current == '-' && next == '-')
                {
                    builder.Append(' ');
                    builder.Append(' ');
                    index++;
                    inLineComment = true;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    builder.Append(' ');
                    builder.Append(' ');
                    index++;
                    inBlockComment = true;
                    continue;
                }
            }

            if (!inDoubleQuote && !inBracket && !inBacktick && current == '\'')
            {
                inSingleQuote = !inSingleQuote;
                builder.Append(' ');
                continue;
            }

            if (!inSingleQuote && !inBracket && !inBacktick && current == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                builder.Append(' ');
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBacktick && current == '[')
            {
                inBracket = true;
                builder.Append(' ');
                continue;
            }

            if (inBracket && current == ']')
            {
                inBracket = false;
                builder.Append(' ');
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBracket && current == '`')
            {
                inBacktick = !inBacktick;
                builder.Append(' ');
                continue;
            }

            builder.Append(inSingleQuote || inDoubleQuote || inBracket || inBacktick ? ' ' : current);
        }

        return builder.ToString();
    }

    private static List<string> TokenizeSqlSegment(string segment)
    {
        List<string> tokens = [];
        StringBuilder builder = new StringBuilder();
        foreach (char current in segment)
        {
            if (IsIdentifierPart(current))
            {
                builder.Append(current);
                continue;
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }

            if (current == '.' || current == ',' || current == '(' || current == ')')
            {
                tokens.Add(current.ToString());
            }
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }
}
