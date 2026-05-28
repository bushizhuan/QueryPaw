namespace SqlAnalyzer.Core.Models;

public sealed class TableCheckDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
