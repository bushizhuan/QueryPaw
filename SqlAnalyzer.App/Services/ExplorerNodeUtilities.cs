using System;
using System.Collections.Generic;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class ExplorerNodeUtilities
{
    private const string SavedConnectionKeyPrefix = "saved-connection:";

    public static bool TryGetConnectionIdFromNodeKey(string? key, out string connectionId)
    {
        connectionId = string.Empty;
        if (string.IsNullOrWhiteSpace(key) ||
            !key.StartsWith(SavedConnectionKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        connectionId = key[SavedConnectionKeyPrefix.Length..];
        return !string.IsNullOrWhiteSpace(connectionId);
    }

    public static string ExtractConnectionIdFromNodeKey(string key)
    {
        return TryGetConnectionIdFromNodeKey(key, out string connectionId) ? connectionId : string.Empty;
    }

    public static void ConfigureConnectionRoot(
        ObjectNode node,
        ConnectionProfile profile,
        DatabaseProviderDefinition? provider,
        bool isConnected,
        bool isActive,
        Func<string, string?, bool> isSchemaOpen)
    {
        node.Name = profile.Name;
        node.DisplayName = profile.Name;
        node.Description = $"{profile.ProviderName} / {profile.Server} / {profile.Database}";
        node.Type = "connection";
        node.Key = SavedConnectionKeyPrefix + profile.Id;
        node.ParentKey = string.Empty;
        node.ConnectionProfileId = profile.Id;
        node.ProviderName = profile.ProviderName;
        node.IsConnected = isConnected;
        node.IsSchemaOpened = false;

        if (!isConnected)
        {
            node.Children.Clear();
            node.IsExpanded = false;
            node.IsLoaded = true;
            node.HasUnloadedChildren = false;
            return;
        }

        bool isDocumentProvider = string.Equals(provider?.Kind, "Document", StringComparison.OrdinalIgnoreCase);
        if (!node.IsLoaded && node.Children.Count == 0)
        {
            node.HasUnloadedChildren = !isDocumentProvider;
        }

        node.IsExpanded = node.IsExpanded || isActive;
        if (isDocumentProvider)
        {
            node.IsLoaded = true;
            node.HasUnloadedChildren = false;
        }

        ApplySubtreeState(node, profile.Id, isConnected, isSchemaOpen);
    }

    public static void ApplySubtreeState(
        ObjectNode node,
        string connectionProfileId,
        bool isConnected,
        Func<string, string?, bool> isSchemaOpen)
    {
        node.ConnectionProfileId = connectionProfileId;
        node.IsConnected = isConnected;
        if (node.IsSchemaNode)
        {
            node.IsSchemaOpened = isConnected && isSchemaOpen(connectionProfileId, node.Name);
        }

        foreach (ObjectNode child in node.Children)
        {
            ApplySubtreeState(child, connectionProfileId, isConnected, isSchemaOpen);
        }
    }

    public static ObjectNode? FindSchemaNodeByConnectionAndName(IEnumerable<ObjectNode> nodes, string connectionProfileId, string schemaName)
    {
        foreach (ObjectNode node in nodes)
        {
            if (node.IsSchemaNode &&
                string.Equals(node.ConnectionProfileId, connectionProfileId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(SchemaSelection.Normalize(node.Name), schemaName, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            ObjectNode? match = FindSchemaNodeByConnectionAndName(node.Children, connectionProfileId, schemaName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    public static ObjectNode? FindOpenedSchemaNode(IEnumerable<ObjectNode> nodes, string connectionProfileId, string schemaName)
    {
        foreach (ObjectNode node in nodes)
        {
            if (node.IsSchemaNode &&
                node.IsSchemaOpened &&
                string.Equals(node.ConnectionProfileId, connectionProfileId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(SchemaSelection.Normalize(node.Name), schemaName, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            ObjectNode? match = FindOpenedSchemaNode(node.Children, connectionProfileId, schemaName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    public static string BuildOpenedSchemaKey(string connectionProfileId, string schemaName)
    {
        return $"{connectionProfileId}|{schemaName.Trim()}";
    }

    public static (string ConnectionProfileId, string SchemaName) SplitOpenedSchemaKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return (string.Empty, string.Empty);
        }

        int separatorIndex = key.IndexOf('|');
        return separatorIndex < 0
            ? (key, string.Empty)
            : (key[..separatorIndex], key[(separatorIndex + 1)..]);
    }

    public static ObjectNode? FindNodeByKey(IEnumerable<ObjectNode> nodes, string key)
    {
        foreach (ObjectNode node in nodes)
        {
            if (string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            ObjectNode? match = FindNodeByKey(node.Children, key);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
}
