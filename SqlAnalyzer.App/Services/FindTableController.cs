using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services; 

public sealed class FindTableController
{
    // 定位表严格限定在当前文档的模式内，匹配顺序按精确命中优先，再回退到本地化/模糊命中。
    public async Task<FindTableResult> FindAsync(MainWindowViewModel viewModel, string selectedText)
    {
        string normalizedSelection = selectedText.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSelection))
        {
            return Failed("请先在编辑器中选中要定位的表名或视图名。");
        }

        (string? requestedSchema, string objectName) = ParseSelectedObjectName(normalizedSelection);
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return Failed("未能从选中文本中识别出对象名称。");
        }

        ConnectionProfile? targetProfile = viewModel.SelectedDocumentConnectionProfile ?? viewModel.ActiveConnectionProfile;
        if (targetProfile == null)
        {
            return Failed("当前标签页未绑定数据库连接。");
        }

        string? currentSchema = ResolveCurrentSchema(viewModel, targetProfile);
        if (!string.IsNullOrWhiteSpace(requestedSchema) &&
            !string.IsNullOrWhiteSpace(currentSchema) &&
            !string.Equals(requestedSchema, currentSchema, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(BuildNotFoundMessage(normalizedSelection, currentSchema));
        }

        try
        {
            if (!viewModel.IsConnectionProfileConnected(targetProfile))
            {
                await viewModel.SetActiveConnectionAsync(targetProfile);
            }

            ObjectNode? rootNode = viewModel.ExplorerNodes.FirstOrDefault(node =>
                string.Equals(node.Type, "connection", StringComparison.OrdinalIgnoreCase) &&
                node.IsConnected &&
                string.Equals(node.Key, "saved-connection:" + targetProfile.Id, StringComparison.OrdinalIgnoreCase));
            if (rootNode == null)
            {
                return Failed("当前连接尚未加载对象树。");
            }

            await viewModel.EnsureNodeChildrenLoadedAsync(rootNode);

            ObjectNode? match = null;
            if (!string.IsNullOrWhiteSpace(currentSchema))
            {
                match = await FindExplorerObjectNodeAsync(viewModel, rootNode, currentSchema, objectName);
                match ??= await FindByLocalizedRelationMatchAsync(
                    viewModel,
                    rootNode,
                    normalizedSelection,
                    objectName,
                    targetProfile,
                    currentSchema);
            }

            if (match == null)
            {
                return Failed(BuildNotFoundMessage(normalizedSelection, currentSchema));
            }

            ExpandAncestors(viewModel.ExplorerNodes, match);
            return new FindTableResult
            {
                Success = true,
                Match = match
            };
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }
    }
    private static FindTableResult Failed(string message)
    {
        return new FindTableResult
        {
            Success = false,
            Message = message
        };
    }
    private static async Task<ObjectNode?> FindExplorerObjectNodeAsync(
        MainWindowViewModel viewModel,
        ObjectNode rootNode,
        string schemaName,
        string objectName)
    {
        StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        IEnumerable<ObjectNode> schemaNodes = rootNode.Children
            .Where(node => string.Equals(node.Type, "schema", comparison))
            .Where(node =>
                string.Equals(node.Name, schemaName, comparison) ||
                string.Equals(node.DisplayName, schemaName, comparison) ||
                string.Equals(node.DisplayText, schemaName, comparison));

        foreach (ObjectNode schemaNode in schemaNodes)
        {
            await viewModel.EnsureNodeChildrenLoadedAsync(schemaNode);
            foreach (ObjectNode folder in OrderRelationFolders(schemaNode.Children.Where(IsRelationFolder)))
            {
                await viewModel.EnsureNodeChildrenLoadedAsync(folder);
                ObjectNode? found = folder.Children.FirstOrDefault(node => MatchesObjectNodeExactly(node, objectName));
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }
    private static async Task<ObjectNode?> FindByLocalizedRelationMatchAsync(
        MainWindowViewModel viewModel,
        ObjectNode rootNode,
        string selectedText,
        string objectName,
        ConnectionProfile targetProfile,
        string currentSchema)
    {
        IReadOnlyList<CompletionEntry> candidates = await viewModel.SearchRelationCandidatesAsync(
            selectedText,
            targetProfile,
            currentSchema);

        foreach (CompletionEntry candidate in candidates
                     .OrderBy(candidate => RankCandidate(candidate, selectedText, objectName))
                     .ThenBy(candidate => GetKindPriority(candidate.Kind))
                     .ThenBy(candidate => candidate.SortWeight)
                     .ThenBy(candidate => candidate.DisplayText, StringComparer.OrdinalIgnoreCase))
        {
            (string? schemaName, string candidateObjectName) = ParseSourceObject(candidate.SourceObject);
            if (string.IsNullOrWhiteSpace(candidateObjectName) ||
                string.IsNullOrWhiteSpace(schemaName) ||
                !string.Equals(schemaName, currentSchema, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ObjectNode? match = await FindExplorerObjectNodeAsync(viewModel, rootNode, schemaName, candidateObjectName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
    private static int RankCandidate(CompletionEntry candidate, string selectedText, string objectName)
    {
        string normalized = selectedText.Trim();
        string normalizedObject = objectName.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 99;
        }

        if (string.Equals(candidate.InsertText, normalizedObject, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (candidate.MatchKeys.Any(key => string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(key, normalizedObject, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (string.Equals(candidate.DisplayText, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.DisplayText, normalizedObject, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (candidate.MatchKeys.Any(key => key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                                           key.StartsWith(normalizedObject, StringComparison.OrdinalIgnoreCase)) ||
            candidate.DisplayText.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
            candidate.DisplayText.StartsWith(normalizedObject, StringComparison.OrdinalIgnoreCase) ||
            candidate.InsertText.StartsWith(normalizedObject, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (candidate.MatchKeys.Any(key => key.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                                           key.Contains(normalizedObject, StringComparison.OrdinalIgnoreCase)) ||
            candidate.DisplayText.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            candidate.DisplayText.Contains(normalizedObject, StringComparison.OrdinalIgnoreCase) ||
            candidate.InsertText.Contains(normalizedObject, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return 5;
    }
    private static int GetKindPriority(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "table" => 0,
            "materializedview" => 1,
            "view" => 2,
            "synonym" => 3,
            _ => 9
        };
    }
    private static IEnumerable<ObjectNode> OrderRelationFolders(IEnumerable<ObjectNode> folders)
    {
        return folders.OrderBy(folder => folder.Key.EndsWith(":tables", StringComparison.OrdinalIgnoreCase) ? 0
            : folder.Key.EndsWith(":materializedviews", StringComparison.OrdinalIgnoreCase) ? 1
            : folder.Key.EndsWith(":views", StringComparison.OrdinalIgnoreCase) ? 2
            : folder.Key.EndsWith(":synonyms", StringComparison.OrdinalIgnoreCase) ? 3
            : 9);
    }
    private static bool MatchesObjectNodeExactly(ObjectNode node, string objectName)
    {
        return string.Equals(node.Name, objectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(node.DisplayName, objectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(node.DisplayText, objectName, StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsRelationFolder(ObjectNode node)
    {
        if (!string.Equals(node.Type, "folder", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return node.Key.EndsWith(":tables", StringComparison.OrdinalIgnoreCase) ||
               node.Key.EndsWith(":views", StringComparison.OrdinalIgnoreCase) ||
               node.Key.EndsWith(":materializedviews", StringComparison.OrdinalIgnoreCase) ||
               node.Key.EndsWith(":synonyms", StringComparison.OrdinalIgnoreCase);
    }
    private static void ExpandAncestors(IReadOnlyList<ObjectNode> roots, ObjectNode node)
    {
        string? currentKey = node.ParentKey;
        while (!string.IsNullOrWhiteSpace(currentKey))
        {
            ObjectNode? parentNode = FindNodeByKey(roots, currentKey);
            if (parentNode == null)
            {
                break;
            }

            parentNode.IsExpanded = true;
            currentKey = parentNode.ParentKey;
        }
    }
    private static ObjectNode? FindNodeByKey(IEnumerable<ObjectNode> nodes, string key)
    {
        foreach (ObjectNode node in nodes)
        {
            if (string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            ObjectNode? found = FindNodeByKey(node.Children, key);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
    private static string? ResolveCurrentSchema(MainWindowViewModel viewModel, ConnectionProfile profile)
    {
        string? selectedSchema = NormalizeSchema(viewModel.SelectedDocumentSchema);
        if (!string.IsNullOrWhiteSpace(selectedSchema))
        {
            return selectedSchema;
        }

        return NormalizeSchema(profile.Schema);
    }
    private static string BuildNotFoundMessage(string objectName, string? currentSchema)
    {
        return string.IsNullOrWhiteSpace(currentSchema)
            ? $"当前模式下未找到对应表：{objectName}"
            : $"当前模式“{currentSchema}”下未找到对应表：{objectName}";
    }
    private static string? NormalizeSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema) ||
            string.Equals(schema, "(Default)", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return schema.Trim();
    }
    private static (string? SchemaName, string ObjectName) ParseSelectedObjectName(string selectedText)
    {
        string trimmed = selectedText.Trim().Trim(';').Trim();
        trimmed = trimmed.Trim('"', '\'', '`');
        trimmed = trimmed.Replace("[", string.Empty).Replace("]", string.Empty);

        string[] parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return (parts[^2], parts[^1]);
        }

        return (null, trimmed);
    }
    private static (string? SchemaName, string ObjectName) ParseSourceObject(string sourceObject)
    {
        if (string.IsNullOrWhiteSpace(sourceObject))
        {
            return (null, string.Empty);
        }

        string[] parts = sourceObject.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            >= 2 => (parts[^2], parts[^1]),
            1 => (null, parts[0]),
            _ => (null, string.Empty)
        };
    }
}
