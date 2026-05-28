namespace SqlAnalyzer.Core.Models;

public sealed class ModelDiagramWorkspace
{
    public string ProviderName { get; set; } = string.Empty;

    public string ConnectionProfileId { get; set; } = string.Empty;

    public string ConnectionName { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public DateTimeOffset LoadedAt { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<ModelTableNode> Tables { get; set; } = Array.Empty<ModelTableNode>();

    public IReadOnlyList<ModelRelationEdge> Relations { get; set; } = Array.Empty<ModelRelationEdge>();
}
