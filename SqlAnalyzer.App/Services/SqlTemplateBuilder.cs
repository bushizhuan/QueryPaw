using System;
using System.Collections.Generic;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class SqlTemplateBuilder
{
    public static string BuildObjectTemplate(string targetName, string templateKind)
    {
        return (templateKind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "count" => $"select count(*) as total_count from {targetName};",
            "insert" => $"insert into {targetName} (...) values (...);",
            "update" => $"update {targetName} set ... where ...;",
            _ => $"select * from {targetName};"
        };
    }

    public static string BuildModelDiagramRelationQuery(string schemaName, ModelDiagramRelationState relation)
    {
        string parentAlias = "p";
        string childAlias = "c";
        string parentTable = QualifyObjectName(schemaName, relation.FromTable);
        string childTable = QualifyObjectName(schemaName, relation.ToTable);
        IReadOnlyList<string> parentColumns = relation.ParentColumns.Count > 0
            ? relation.ParentColumns
            : SplitRelationColumns(relation.ParentColumnsText);
        IReadOnlyList<string> childColumns = relation.ChildColumns.Count > 0
            ? relation.ChildColumns
            : SplitRelationColumns(relation.ChildColumnsText);
        List<string> joinConditions = [];
        int pairCount = Math.Min(parentColumns.Count, childColumns.Count);
        for (int index = 0; index < pairCount; index++)
        {
            joinConditions.Add($"{childAlias}.{childColumns[index]} = {parentAlias}.{parentColumns[index]}");
        }

        if (joinConditions.Count == 0)
        {
            joinConditions.Add("1 = 1");
        }

        return string.Join(Environment.NewLine, new[]
        {
            "select *",
            $"from {childTable} {childAlias}",
            $"join {parentTable} {parentAlias}",
            $"  on {string.Join(Environment.NewLine + " and ", joinConditions)}"
        });
    }

    public static string ResolveQueryTargetName(ObjectNode? node)
    {
        if (node == null)
        {
            return string.Empty;
        }

        if (string.Equals(node.Type, "table", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Type, "view", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Type, "materializedview", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(node.SchemaName) ? node.Name : node.SchemaName + "." + node.Name;
        }

        if (string.Equals(node.Type, "column", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(node.ParentKey))
        {
            string[] parts = node.ParentKey.Split(':', 3);
            if (parts.Length == 3 &&
                (string.Equals(parts[0], "table", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[0], "view", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[0], "materializedview", StringComparison.OrdinalIgnoreCase)))
            {
                return parts[1] + "." + parts[2];
            }
        }

        return string.Empty;
    }

    private static string QualifyObjectName(string schemaName, string objectName)
    {
        return string.IsNullOrWhiteSpace(schemaName) ? objectName : $"{schemaName}.{objectName}";
    }

    private static IReadOnlyList<string> SplitRelationColumns(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
