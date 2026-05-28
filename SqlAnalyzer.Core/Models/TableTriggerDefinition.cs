namespace SqlAnalyzer.Core.Models;

public sealed class TableTriggerDefinition
{
    public string Name { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
}
