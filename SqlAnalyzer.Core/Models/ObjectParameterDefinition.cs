namespace SqlAnalyzer.Core.Models;

public sealed class ObjectParameterDefinition
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public string Direction { get; set; } = string.Empty;

    public string DefaultValue { get; set; } = string.Empty;
}
