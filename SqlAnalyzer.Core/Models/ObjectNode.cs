using System;
using System.Collections.ObjectModel;

namespace SqlAnalyzer.Core.Models;

public sealed class ObjectNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ParentKey { get; set; } = string.Empty;
    public string ConnectionProfileId { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public bool IsSchemaOpened { get; set; }
    public bool IsExpanded { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public bool HasUnloadedChildren { get; set; }
    public bool IsLoaded { get; set; }
    public ObservableCollection<ObjectNode> Children { get; set; } = [];

    public string DisplayText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return Name;
            }

            if (string.Equals(DisplayName, Name, StringComparison.OrdinalIgnoreCase))
            {
                return Name;
            }

            return $"{DisplayName} ({Name})";
        }
    }

    public string IconGlyph => Type switch
    {
        "connection" => ResolveConnectionGlyph(),
        "folder" => "[]",
        "schema" => "SC",
        "table" => "TB",
        "view" => "VW",
        "materializedview" => "MV",
        "sequence" => "SQ",
        "trigger" => "TR",
        "synonym" => "SY",
        "package" => "PK",
        "function" => "FN",
        "procedure" => "SP",
        "column" => "CL",
        "status" => "..",
        "error" => "!!",
        "empty" => "--",
        _ => "DB"
    };

    public string IconColor => Type switch
    {
        "connection" => ResolveConnectionIconColor(),
        "folder" => "#2F80D8",
        "schema" => IsSchemaOpened ? "#2563EB" : "#5B8FC8",
        "table" => "#2F80D8",
        "view" => "#3B82C4",
        "materializedview" => "#2F80D8",
        "sequence" => "#4F8FCF",
        "trigger" => "#3C7BC2",
        "synonym" => "#348FBE",
        "package" => "#4B7FD1",
        "function" => "#4B7FD1",
        "procedure" => "#4B7FD1",
        "column" => "#5F92C8",
        "status" => "#6D9CCD",
        "error" => "#D35D6E",
        "empty" => "#8FA2B7",
        _ => "#6D9CCD"
    };

    public string IconKind => Type switch
    {
        "connection" => ResolveConnectionIconKind(),
        "folder" => "Folder",
        "schema" => "Schema",
        "table" => "Table",
        "view" => "View",
        "materializedview" => "MaterializedView",
        "sequence" => "Sequence",
        "trigger" => "Trigger",
        "synonym" => "Synonym",
        "package" => "Package",
        "function" => "Function",
        "procedure" => "Procedure",
        "column" => "Column",
        "status" => "Plan",
        "error" => "Error",
        "empty" => "Empty",
        _ => "Database"
    };

    public string IconTileBackground => Type switch
    {
        "connection" => ResolveConnectionTileBackground(),
        "schema" => IsSchemaOpened ? "#DBEAFE" : "#EEF6FF",
        "error" => "#FFF1F2",
        "empty" => "#F8FAFC",
        _ => "#EEF7FF"
    };

    public string IconTileBorder => Type switch
    {
        "connection" => ResolveConnectionTileBorder(),
        "schema" => IsSchemaOpened ? "#AFCBF7" : "#CBE2F8",
        "error" => "#F4C2C9",
        _ => "#CBE2F8"
    };

    public string TextColor => Type switch
    {
        "connection" => IsConnected ? "#2F6FDB" : "#9CA3AF",
        "error" => "#C45454",
        "status" => "#6D88A4",
        "empty" => "#9CA3AF",
        _ => "#1F2937"
    };

    public bool IsConnectionNode => string.Equals(Type, "connection", StringComparison.OrdinalIgnoreCase);

    public bool IsSchemaNode => string.Equals(Type, "schema", StringComparison.OrdinalIgnoreCase);

    public bool CanOpenConnection => IsConnectionNode && !IsConnected;

    public bool CanCloseConnection => IsConnectionNode && IsConnected;

    public bool CanOpenSchema => IsSchemaNode && IsConnected && !IsSchemaOpened;

    public bool CanCloseSchema => IsSchemaNode && IsSchemaOpened;

    public bool CanEditConnection => IsConnectionNode;

    public bool CanDuplicateConnection => IsConnectionNode;

    public bool CanViewConnectionDetails => IsConnectionNode;

    public string ConnectionStatusText => IsConnected ? "已连接" : "未连接";

    public string ConnectionStatusBackground => IsConnected ? "#DCFCE7" : "#F1F5F9";

    public string ConnectionStatusForeground => IsConnected ? "#166534" : "#64748B";

    public string SchemaStatusText => IsSchemaOpened ? "已打开" : "未打开";

    public string SchemaStatusBackground => IsSchemaOpened ? "#DBEAFE" : "#F1F5F9";

    public string SchemaStatusForeground => IsSchemaOpened ? "#1D4ED8" : "#64748B";

    public string SecondaryText
    {
        get
        {
            if (!IsConnectionNode)
            {
                return string.Empty;
            }

            string provider = string.IsNullOrWhiteSpace(ProviderName) ? "Database" : ProviderName;
            return string.IsNullOrWhiteSpace(Description) ? provider : Description;
        }
    }

    public bool IsRetryNode => string.Equals(Type, "error", StringComparison.OrdinalIgnoreCase);

    public bool CanRefreshNode =>
        string.Equals(Type, "connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "folder", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "schema", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "table", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "view", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "materializedview", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "sequence", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "trigger", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "synonym", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "package", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "function", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "procedure", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "column", StringComparison.OrdinalIgnoreCase) ||
        IsRetryNode;

    public bool CanCopyName =>
        !string.Equals(Type, "status", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Type, "empty", StringComparison.OrdinalIgnoreCase);

    public bool CanGenerateSelectSql =>
        string.Equals(Type, "table", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "view", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "materializedview", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "column", StringComparison.OrdinalIgnoreCase);

    public bool CanDesignTable => string.Equals(Type, "table", StringComparison.OrdinalIgnoreCase);

    public bool CanExportTableData =>
        string.Equals(Type, "table", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "view", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "materializedview", StringComparison.OrdinalIgnoreCase);

    public bool CanExportTableStructure =>
        string.Equals(Type, "table", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "view", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "materializedview", StringComparison.OrdinalIgnoreCase);

    public bool CanOpenDetails =>
        string.Equals(Type, "table", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "view", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "materializedview", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "function", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "procedure", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "sequence", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "trigger", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "synonym", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "package", StringComparison.OrdinalIgnoreCase);

    public bool CanEditObjectDefinition =>
        string.Equals(Type, "view", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "function", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "procedure", StringComparison.OrdinalIgnoreCase);

    public bool CanOpenSchemaModelDiagram => IsSchemaNode && IsSchemaOpened;

    public bool CanOpenTableModelDiagram => string.Equals(Type, "table", StringComparison.OrdinalIgnoreCase);
    private string ResolveConnectionGlyph()
    {
        return ProviderName.ToLowerInvariant() switch
        {
            "oracle" => "OR",
            "mysql" => "MY",
            "mariadb" => "MA",
            "sqlserver" => "MS",
            "postgresql" => "PG",
            "kingbasees" => "KB",
            "dameng" => "DM",
            "sqlite" => "SL",
            "mongodb" => "MG",
            _ => IsConnected ? "DB" : "db"
        };
    }

    private string ResolveConnectionIconKind()
    {
        return NormalizeProviderName() switch
        {
            "oracle" => "OracleLogo",
            "mysql" => "MySqlLogo",
            "mariadb" => "MariaDbLogo",
            "sqlserver" => "SqlServerLogo",
            "postgresql" => "PostgreSqlLogo",
            "kingbasees" => "KingbaseLogo",
            "dameng" => "DamengLogo",
            "sqlite" => "SqliteLogo",
            "mongodb" => "MongoDbLogo",
            _ => "Connection"
        };
    }

    private string ResolveConnectionIconColor()
    {
        return NormalizeProviderName() switch
        {
            "oracle" => "#E1261C",
            "mysql" => "#00758F",
            "mariadb" => "#00758F",
            "sqlserver" => "#CC2927",
            "postgresql" => "#336791",
            "kingbasees" => "#C2410C",
            "dameng" => "#1D4ED8",
            "sqlite" => "#2F80D8",
            "mongodb" => "#13AA52",
            _ => IsConnected ? "#2F80D8" : "#94A3B8"
        };
    }

    private string ResolveConnectionTileBackground()
    {
        if (!IsConnected)
        {
            return "#F8FAFC";
        }

        return NormalizeProviderName() switch
        {
            "oracle" => "#FFF1F0",
            "mysql" => "#E6F7FB",
            "mariadb" => "#E6F7FB",
            "sqlserver" => "#FFF1F2",
            "postgresql" => "#EAF3FA",
            "kingbasees" => "#FFF7ED",
            "dameng" => "#EEF4FF",
            "sqlite" => "#EAF5FF",
            "mongodb" => "#ECFDF3",
            _ => "#EAF5FF"
        };
    }

    private string ResolveConnectionTileBorder()
    {
        if (!IsConnected)
        {
            return "#D5DEE8";
        }

        return NormalizeProviderName() switch
        {
            "oracle" => "#F4B2AE",
            "mysql" => "#9BD7E4",
            "mariadb" => "#9BD7E4",
            "sqlserver" => "#F2B4B3",
            "postgresql" => "#B9D0E1",
            "kingbasees" => "#FDBA74",
            "dameng" => "#BFD3FF",
            "sqlite" => "#B8D8F4",
            "mongodb" => "#BBF7D0",
            _ => "#B8D8F4"
        };
    }

    private string NormalizeProviderName()
    {
        return (ProviderName ?? string.Empty).Trim().ToLowerInvariant();
    }
}
