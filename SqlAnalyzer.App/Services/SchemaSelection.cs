using System;
using System.Collections.Generic;
using System.Linq;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class SchemaSelection
{
    private const string DefaultSchemaOption = "(Default)";

    public static string ResolvePreferred(ConnectionProfile? profile, IEnumerable<string> schemas)
    {
        if (profile == null)
        {
            return string.Empty;
        }

        string[] orderedSchemas = schemas.ToArray();
        if (orderedSchemas.Length == 0)
        {
            return string.Empty;
        }

        string userName = profile.UserName?.Trim() ?? string.Empty;
        if (userName.Length > 0)
        {
            string? match = orderedSchemas.FirstOrDefault(item => string.Equals(item, userName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        if (string.Equals(profile.ProviderName, "PostgreSql", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.ProviderName, "KingbaseES", StringComparison.OrdinalIgnoreCase))
        {
            string? match = orderedSchemas.FirstOrDefault(item => string.Equals(item, "public", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        if (string.Equals(profile.ProviderName, "SQLite", StringComparison.OrdinalIgnoreCase))
        {
            string? match = orderedSchemas.FirstOrDefault(item => string.Equals(item, "main", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return string.Empty;
    }

    public static string Normalize(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.Equals(schema, DefaultSchemaOption, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return schema.Trim();
    }

    public static string NormalizeDisplay(string? schema)
    {
        return string.IsNullOrWhiteSpace(schema) ? DefaultSchemaOption : schema.Trim();
    }

    public static string ResolveInitial(EditorDocument? document)
    {
        string schema = Normalize(document?.DefaultSchema);
        return string.IsNullOrWhiteSpace(schema) ? DefaultSchemaOption : schema;
    }

    public static string ResolveDocumentEffectiveSchema(EditorDocument document, ConnectionProfile profile)
    {
        string documentSchema = Normalize(document.DefaultSchema);
        if (!string.IsNullOrWhiteSpace(documentSchema))
        {
            return documentSchema;
        }

        if (!string.IsNullOrWhiteSpace(profile.Schema))
        {
            return profile.Schema.Trim();
        }

        return profile.ProviderName switch
        {
            "MySql" or "MariaDB" => profile.Database?.Trim() ?? string.Empty,
            "PostgreSql" or "KingbaseES" => "public",
            "Oracle" or "Dameng" => profile.UserName?.Trim() ?? string.Empty,
            "SqlServer" => "dbo",
            "SQLite" => "main",
            _ => string.Empty
        };
    }

    public static bool Contains(IEnumerable<string> schemas, string schema)
    {
        return schemas.Any(item => string.Equals(item, schema, StringComparison.OrdinalIgnoreCase));
    }
}
