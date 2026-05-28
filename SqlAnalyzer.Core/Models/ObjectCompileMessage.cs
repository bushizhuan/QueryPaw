namespace SqlAnalyzer.Core.Models;

public sealed class ObjectCompileMessage
{
    public int Line { get; set; }

    public int Column { get; set; }

    public string Severity { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
