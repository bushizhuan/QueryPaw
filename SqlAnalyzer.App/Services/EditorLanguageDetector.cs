using System;

namespace SqlAnalyzer.App.Services;

internal static class EditorLanguageDetector
{
    private const int ProbeLength = 4096;
    public static string DetectExtension(string? text)
    {
        string content = BuildProbe(text);
        if (content.Length == 0)
        {
            return ".sql";
        }

        if (LooksLikeXml(content))
        {
            return ".xml";
        }

        if (LooksLikeJson(content))
        {
            return ".json";
        }

        if (LooksLikeCSharp(content))
        {
            return ".cs";
        }

        if (LooksLikeJava(content))
        {
            return ".java";
        }

        return ".sql";
    }

    private static string BuildProbe(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        if (start >= text.Length)
        {
            return string.Empty;
        }

        int length = Math.Min(ProbeLength, text.Length - start);
        return text.Substring(start, length);
    }
    private static bool LooksLikeXml(string text)
    {
        return text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
               (text.StartsWith("<", StringComparison.Ordinal) && text.Contains("</", StringComparison.Ordinal));
    }
    private static bool LooksLikeJson(string text)
    {
        return (text.StartsWith("{", StringComparison.Ordinal) && text.Contains(':', StringComparison.Ordinal)) ||
               (text.StartsWith("[", StringComparison.Ordinal) && text.Contains('{', StringComparison.Ordinal));
    }
    private static bool LooksLikeCSharp(string text)
    {
        return text.Contains("namespace ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("using ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("public class ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("private ", StringComparison.OrdinalIgnoreCase);
    }
    private static bool LooksLikeJava(string text)
    {
        return text.Contains("package ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("import java", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("public class ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("public static void main", StringComparison.OrdinalIgnoreCase);
    }
}
