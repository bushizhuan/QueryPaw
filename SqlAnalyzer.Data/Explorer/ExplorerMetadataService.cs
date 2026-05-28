using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.Data.Explorer;

public sealed class ExplorerMetadataService
{
    private readonly ILocalizationResolver _localizationResolver;
    public ExplorerMetadataService(ILocalizationResolver localizationResolver)
    {
        _localizationResolver = localizationResolver;
    }
    public Task<IReadOnlyList<ObjectNode>> LoadRootNodesAsync(ConnectionProfile profile, DatabaseProviderDefinition provider)
    {
        IReadOnlyList<ObjectNode> nodes =
        [
            new ObjectNode
            {
                Name = profile.Name,
                DisplayName = profile.Name,
                Description = $"{provider.DisplayName} / {profile.Server} / {profile.Database}",
                Type = "connection",
                Key = "connection",
                IsConnected = true,
                IsExpanded = true,
                ProviderName = provider.Name,
                HasUnloadedChildren = !string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase)
            }
        ];

        return Task.FromResult(nodes);
    }
    public async Task<IReadOnlyList<ObjectNode>> LoadChildNodesAsync(
        ConnectionProfile profile,
        ObjectNode node,
        DatabaseProviderDefinition provider,
        Func<CancellationToken, Task<IReadOnlyList<string>>> loadSchemasAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadTablesAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadViewsAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadMaterializedViewsAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadFunctionsAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadProceduresAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadSequencesAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadTriggersAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadSynonymsAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadPackagesAsync,
        CancellationToken cancellationToken = default)
    {
        LocalizationDictionarySnapshot localizationSnapshot = await _localizationResolver.GetSnapshotAsync(profile, cancellationToken: cancellationToken);
        if (string.Equals(provider.Kind, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ObjectNode>();
        }

        if (string.Equals(node.Type, "connection", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<string> schemas = await loadSchemasAsync(cancellationToken);
            return schemas.Select(schema => new ObjectNode
            {
                Name = schema,
                DisplayName = schema,
                Description = $"{provider.DisplayName} schema",
                Type = "schema",
                Key = $"{node.Key}:schema:{schema}",
                ParentKey = node.Key,
                ConnectionProfileId = profile.Id,
                SchemaName = schema,
                IsConnected = true,
                ProviderName = profile.ProviderName,
                HasUnloadedChildren = true
            }).ToArray();
        }

        if (string.Equals(node.Type, "schema", StringComparison.OrdinalIgnoreCase))
        {
            string schema = node.SchemaName;
            List<ObjectNode> folders =
            [
                CreateFolderNode("Tables", "tables", schema, node.Key, profile.ProviderName),
                CreateFolderNode("Views", "views", schema, node.Key, profile.ProviderName)
            ];

            if (provider.Capabilities.SupportsMaterializedViews)
            {
                folders.Add(CreateFolderNode("Materialized Views", "materializedviews", schema, node.Key, profile.ProviderName));
            }

            if (provider.Capabilities.SupportsFunctions)
            {
                folders.Add(CreateFolderNode("Functions", "functions", schema, node.Key, profile.ProviderName));
            }

            if (provider.Capabilities.SupportsProcedures)
            {
                folders.Add(CreateFolderNode("Procedures", "procedures", schema, node.Key, profile.ProviderName));
            }

            if (provider.Capabilities.SupportsSequences)
            {
                folders.Add(CreateFolderNode("Sequences", "sequences", schema, node.Key, profile.ProviderName));
            }

            if (provider.Capabilities.SupportsTriggers)
            {
                folders.Add(CreateFolderNode("Triggers", "triggers", schema, node.Key, profile.ProviderName));
            }

            if (provider.Capabilities.SupportsSynonyms)
            {
                folders.Add(CreateFolderNode("Synonyms", "synonyms", schema, node.Key, profile.ProviderName));
            }

            if (provider.Capabilities.SupportsPackages)
            {
                folders.Add(CreateFolderNode("Packages", "packages", schema, node.Key, profile.ProviderName));
            }

            return folders;
        }

        if (!string.Equals(node.Type, "folder", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ObjectNode>();
        }

        string folderSchema = node.SchemaName;
        return node.Key.Split(':').Last().ToLowerInvariant() switch
            {
            "tables" => (await loadTablesAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "table", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "views" => (await loadViewsAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "view", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "materializedviews" => (await loadMaterializedViewsAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "materializedview", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "functions" => (await loadFunctionsAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "function", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "procedures" => (await loadProceduresAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "procedure", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "sequences" => (await loadSequencesAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "sequence", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "triggers" => (await loadTriggersAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "trigger", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "synonyms" => (await loadSynonymsAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "synonym", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            "packages" => (await loadPackagesAsync(folderSchema, cancellationToken))
                .Select(name => CreateObjectNode(localizationSnapshot, name, "package", folderSchema, node.Key, profile.ProviderName, false))
                .ToArray(),
            _ => Array.Empty<ObjectNode>()
        };
    }
    private static ObjectNode CreateFolderNode(string name, string suffix, string schema, string parentKey, string providerName)
    {
        return new ObjectNode
        {
            Name = name,
            DisplayName = name,
            Description = $"{schema} / {name}",
            Type = "folder",
            Key = $"{parentKey}:{suffix}",
            ParentKey = parentKey,
            SchemaName = schema,
            IsConnected = true,
            ProviderName = providerName,
            HasUnloadedChildren = true
        };
    }
    private ObjectNode CreateObjectNode(LocalizationDictionarySnapshot localizationSnapshot, string name, string type, string schema, string parentKey, string providerName, bool hasUnloadedChildren)
    {
        string displayName = _localizationResolver.ResolveObjectDisplayName(localizationSnapshot, schema, name, type, name);
        return new ObjectNode
        {
            Name = name,
            DisplayName = displayName,
            Description = $"{schema} / {type}",
            Type = type,
            Key = $"{parentKey}:{type}:{name}",
            ParentKey = parentKey,
            SchemaName = schema,
            IsConnected = true,
            ProviderName = providerName,
            HasUnloadedChildren = hasUnloadedChildren
        };
    }
}
