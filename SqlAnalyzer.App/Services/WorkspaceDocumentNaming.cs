using System;

namespace SqlAnalyzer.App.Services;

public static class WorkspaceDocumentNaming
{
    public static string BuildCommentWorkspaceKey(string connectionProfileId, string schemaDisplay)
    {
        return string.Concat(connectionProfileId?.Trim() ?? string.Empty, "::", SchemaSelection.NormalizeDisplay(schemaDisplay));
    }

    public static string BuildCommentMaintenanceTitle(string connectionName, string schemaDisplay)
    {
        string schemaText = SchemaSelection.NormalizeDisplay(schemaDisplay);
        return string.IsNullOrWhiteSpace(connectionName) ? $"注释维护[{schemaText}]" : $"注释维护[{connectionName}/{schemaText}]";
    }

    public static string BuildModelDiagramWorkspaceKey(string connectionProfileId, string schemaName, string focusTableName)
    {
        return string.Join(
            "::",
            connectionProfileId?.Trim() ?? string.Empty,
            schemaName?.Trim() ?? string.Empty,
            focusTableName?.Trim() ?? string.Empty);
    }

    public static string BuildModelDiagramTitle(string connectionName, string schemaName, string focusTableName)
    {
        if (!string.IsNullOrWhiteSpace(focusTableName))
        {
            return string.IsNullOrWhiteSpace(connectionName)
                ? $"关系图[{schemaName}.{focusTableName}]"
                : $"关系图[{connectionName}/{schemaName}.{focusTableName}]";
        }

        return string.IsNullOrWhiteSpace(connectionName)
            ? $"数据模型[{schemaName}]"
            : $"数据模型[{connectionName}/{schemaName}]";
    }

    public static string BuildObjectEditorWorkspaceKey(string connectionProfileId, string schemaName, string objectType, string objectName)
    {
        return string.Join(
            "::",
            connectionProfileId?.Trim() ?? string.Empty,
            schemaName?.Trim() ?? string.Empty,
            objectType?.Trim() ?? string.Empty,
            objectName?.Trim() ?? string.Empty);
    }

    public static string BuildObjectEditorTitle(string objectName, string objectType)
    {
        string typeText = NormalizeObjectEditorTypeDisplay(objectType);
        return $"{objectName} - {typeText}";
    }

    private static string NormalizeObjectEditorTypeDisplay(string objectType)
    {
        return (objectType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "view" => "视图",
            "procedure" => "存储过程",
            "function" => "函数",
            _ => string.IsNullOrWhiteSpace(objectType) ? "对象" : objectType
        };
    }
}
