namespace SqlAnalyzer.Core.Models;

public sealed class DatabaseProviderDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Kind { get; set; } = "Relational";
    public string DriverFamily { get; set; } = string.Empty;
    public string SupportLevel { get; set; } = "Experimental";
    public string RecommendedDriver { get; set; } = string.Empty;
    public string InvariantName { get; set; } = string.Empty;
    public string FactoryTypeName { get; set; } = string.Empty;
    public IReadOnlyList<string> FactoryTypeAliases { get; set; } = Array.Empty<string>();
    public string DefaultManagedDriver { get; set; } = string.Empty;
    public string DefaultNativeDependency { get; set; } = string.Empty;
    public string ConnectionTemplate { get; set; } = string.Empty;
    public string TestSql { get; set; } = string.Empty;
    public string? ExplainPrefix { get; set; }
    public string? ExplainQuery { get; set; }
    public ProviderCapabilities Capabilities { get; set; } = new();
}
