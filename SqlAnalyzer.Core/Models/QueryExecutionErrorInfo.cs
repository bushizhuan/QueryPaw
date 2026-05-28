namespace SqlAnalyzer.Core.Models;

public sealed class QueryExecutionErrorInfo
{
    public string Message { get; set; } = string.Empty;

    public int StatementIndex { get; set; } = 1;

    public int StatementStartOffset { get; set; }

    public int RelativeLine { get; set; } = 1;

    public int RelativeColumn { get; set; } = 1;

    public int AbsoluteLine { get; set; } = 1;

    public int AbsoluteColumn { get; set; } = 1;

    public int AbsoluteOffset { get; set; } = -1;

    public string ErrorCode { get; set; } = string.Empty;

    public string SqlState { get; set; } = string.Empty;

    public bool IsPositionInferred { get; set; }
}
