using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAnalyzer.App.Services;

public sealed class SchemaWorkspaceStateStore
{
	// 树节点会重建，稳定状态只记连接和 schema。
	private readonly HashSet<string> _openedSchemaKeys = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _rememberedSchemasByConnectionId = new(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyDictionary<string, string> RememberedSchemasByConnectionId => _rememberedSchemasByConnectionId;

	public void RestoreRememberedSchemas(
		IEnumerable<KeyValuePair<string, string>> schemasByConnectionId,
		ISet<string> validConnectionIds,
		Func<string?, string> normalizeSchema)
	{
		_rememberedSchemasByConnectionId.Clear();
		foreach (KeyValuePair<string, string> item in schemasByConnectionId)
		{
			string normalizedConnectionId = item.Key?.Trim() ?? string.Empty;
			string normalizedSchemaName = normalizeSchema(item.Value);
			// 连接被删掉后，旧 schema 记忆也就没有意义了。
			if (!string.IsNullOrWhiteSpace(normalizedConnectionId) &&
				!string.IsNullOrWhiteSpace(normalizedSchemaName) &&
				validConnectionIds.Contains(normalizedConnectionId))
			{
				_rememberedSchemasByConnectionId[normalizedConnectionId] = normalizedSchemaName;
			}
		}
	}

	public void Prune(ISet<string> validConnectionIds)
	{
		_openedSchemaKeys.RemoveWhere(key =>
		{
			(string connectionId, _) = ExplorerNodeUtilities.SplitOpenedSchemaKey(key);
			return !validConnectionIds.Contains(connectionId);
		});

		foreach (string invalidConnectionId in _rememberedSchemasByConnectionId.Keys.Where(key => !validConnectionIds.Contains(key)).ToArray())
		{
			_rememberedSchemasByConnectionId.Remove(invalidConnectionId);
		}
	}

	public bool TryGetRememberedSchema(string connectionProfileId, out string schemaName)
	{
		if (_rememberedSchemasByConnectionId.TryGetValue(connectionProfileId, out string? rememberedSchema) &&
			rememberedSchema != null)
		{
			schemaName = rememberedSchema;
			return true;
		}

		schemaName = string.Empty;
		return false;
	}

	public void RememberSchema(string connectionProfileId, string schemaName)
	{
		if (!string.IsNullOrWhiteSpace(connectionProfileId) && !string.IsNullOrWhiteSpace(schemaName))
		{
			_rememberedSchemasByConnectionId[connectionProfileId] = schemaName;
		}
	}

	public bool ContainsOpenedSchema(string connectionProfileId, string schemaName)
	{
		return !string.IsNullOrWhiteSpace(connectionProfileId) &&
		       !string.IsNullOrWhiteSpace(schemaName) &&
		       _openedSchemaKeys.Contains(ExplorerNodeUtilities.BuildOpenedSchemaKey(connectionProfileId, schemaName));
	}

	public bool MarkSchemaOpen(string connectionProfileId, string schemaName)
	{
		if (string.IsNullOrWhiteSpace(connectionProfileId) || string.IsNullOrWhiteSpace(schemaName))
		{
			return false;
		}

		// 返回值留给调用方判断这次是不是状态真的变了。
		return _openedSchemaKeys.Add(ExplorerNodeUtilities.BuildOpenedSchemaKey(connectionProfileId, schemaName));
	}

	public bool MarkSchemaClosed(string connectionProfileId, string schemaName)
	{
		if (string.IsNullOrWhiteSpace(connectionProfileId) || string.IsNullOrWhiteSpace(schemaName))
		{
			return false;
		}

		return _openedSchemaKeys.Remove(ExplorerNodeUtilities.BuildOpenedSchemaKey(connectionProfileId, schemaName));
	}

	public IReadOnlyList<string> GetOpenedSchemasForConnection(string connectionProfileId)
	{
		return _openedSchemaKeys
			.Select(ExplorerNodeUtilities.SplitOpenedSchemaKey)
			.Where(item => string.Equals(item.ConnectionProfileId, connectionProfileId, StringComparison.OrdinalIgnoreCase))
			.Select(item => item.SchemaName)
			.Where(item => !string.IsNullOrWhiteSpace(item))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public void RemoveOpenedSchemasForConnection(string connectionProfileId)
	{
		if (string.IsNullOrWhiteSpace(connectionProfileId))
		{
			return;
		}

		_openedSchemaKeys.RemoveWhere(key =>
		{
			(string storedConnectionId, _) = ExplorerNodeUtilities.SplitOpenedSchemaKey(key);
			return string.Equals(storedConnectionId, connectionProfileId, StringComparison.OrdinalIgnoreCase);
		});
	}
}
