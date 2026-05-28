using System.Collections.Generic;

namespace SqlAnalyzer.Core.Models;

public sealed class TableDesignModel
{
    public string ProviderName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string TableComment { get; set; } = string.Empty;
    public bool SupportsDirectSave { get; set; }
    public string CapabilityLevel { get; set; } = "PreviewOnly";
    public List<TableColumnDefinition> Columns { get; set; } = [];
    public List<TableIndexDefinition> Indexes { get; set; } = [];
    public List<TableIndexDefinition> UniqueKeys { get; set; } = [];
    public List<TableForeignKeyDefinition> ForeignKeys { get; set; } = [];
    public List<TableCheckDefinition> Checks { get; set; } = [];
    public List<TableTriggerDefinition> Triggers { get; set; } = [];
    public List<TableOptionDefinition> Options { get; set; } = [];
}
