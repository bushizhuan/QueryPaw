namespace SqlAnalyzer.Core.Models;

public sealed class ProviderCapabilities
{
    public bool SupportsExplain { get; init; }
    public bool SupportsExportInsert { get; init; }
    public bool SupportsDataEdit { get; init; }
    public bool SupportsDirectTableAlter { get; init; }
    public bool SupportsNativeDependency { get; init; }
    public bool SupportsMaterializedViews { get; init; }
    public bool SupportsSequences { get; init; }
    public bool SupportsPackages { get; init; }
    public bool SupportsFunctions { get; init; } = true;
    public bool SupportsProcedures { get; init; }
    public bool SupportsTriggers { get; init; }
    public bool SupportsSynonyms { get; init; }
}
