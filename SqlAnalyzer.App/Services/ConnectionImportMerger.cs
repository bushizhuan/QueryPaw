using System;
using System.Collections.Generic;
using System.Linq;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class ConnectionImportMergeOutcome
{
	public ConnectionImportResult Result { get; init; } = new();

	public ConnectionProfile? FirstImportedProfile { get; init; }

	// 替换的正好是当前连接时，界面上的活动连接也要换成新内容。
	public ConnectionProfile? ReplacementActiveConnectionProfile { get; init; }
}

public static class ConnectionImportMerger
{
	public static ConnectionImportMergeOutcome MergePreviewItems(
		IList<ConnectionProfile> connections,
		IEnumerable<ConnectionImportPreviewItem>? previewItems,
		ConnectionProfile? activeConnectionProfile)
	{
		ConnectionImportResult result = new();
		ConnectionProfile? firstImportedProfile = null;
		ConnectionProfile? replacementActiveConnectionProfile = null;

		foreach (ConnectionImportPreviewItem previewItem in previewItems ?? Array.Empty<ConnectionImportPreviewItem>())
		{
			if (!previewItem.IsImportable || IsAction(previewItem, "Skip"))
			{
				result.SkippedCount++;
				continue;
			}

			ConnectionProfile profile = ConnectionProfileUtilities.Clone(previewItem.Profile);
			ConnectionProfileUtilities.NormalizeOracleSettings(profile);
			if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.ProviderName))
			{
				result.SkippedCount++;
				continue;
			}

			// 导入预览页已经让用户选过处理方式，这里只按结果落地。
			ConnectionProfile? existingProfile = FindConflict(connections, profile);
			if (existingProfile != null)
			{
				if (IsAction(previewItem, "Replace"))
				{
					ConnectionProfileUtilities.CopyValues(existingProfile, profile, preserveId: true);
					ConnectionProfileUtilities.NormalizeOracleSettings(existingProfile);
					ConnectionProfileUtilities.ApplyVisuals(existingProfile);
					if (activeConnectionProfile != null &&
						string.Equals(activeConnectionProfile.Id, existingProfile.Id, StringComparison.OrdinalIgnoreCase))
					{
						replacementActiveConnectionProfile = ConnectionProfileUtilities.Clone(existingProfile);
					}

					firstImportedProfile ??= existingProfile;
					result.ReplacedCount++;
					continue;
				}

				if (IsAction(previewItem, "Rename") || IsAction(previewItem, "Add"))
				{
					profile.Name = BuildUniqueName(connections, string.IsNullOrWhiteSpace(previewItem.ResolvedName) ? profile.Name : previewItem.ResolvedName);
					EnsureConnectionId(profile, connections);
					ConnectionProfileUtilities.ApplyVisuals(profile);
					connections.Add(profile);
					firstImportedProfile ??= profile;
					result.RenamedCount++;
					continue;
				}

				result.SkippedCount++;
				continue;
			}

			EnsureConnectionId(profile, connections);
			ConnectionProfileUtilities.ApplyVisuals(profile);
			connections.Add(profile);
			firstImportedProfile ??= profile;
			result.AddedCount++;
		}

		return new ConnectionImportMergeOutcome
		{
			Result = result,
			FirstImportedProfile = firstImportedProfile,
			ReplacementActiveConnectionProfile = replacementActiveConnectionProfile
		};
	}

	private static ConnectionProfile? FindConflict(IEnumerable<ConnectionProfile> connections, ConnectionProfile profile)
	{
		// Id 可能来自另一台机器，判断冲突时看用户能感知的连接身份。
		return connections.FirstOrDefault(item =>
			string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(item.ProviderName, profile.ProviderName, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(ConnectionProfileUtilities.BuildIdentityEndpoint(item), ConnectionProfileUtilities.BuildIdentityEndpoint(profile), StringComparison.OrdinalIgnoreCase));
	}

	private static void EnsureConnectionId(ConnectionProfile profile, IEnumerable<ConnectionProfile> connections)
	{
		if (string.IsNullOrWhiteSpace(profile.Id) || connections.Any(item => string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase)))
		{
			profile.Id = Guid.NewGuid().ToString("N");
		}
	}

	private static string BuildUniqueName(IEnumerable<ConnectionProfile> connections, string baseName)
	{
		HashSet<string> names = connections
			.Select(item => item.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		string normalizedBaseName = string.IsNullOrWhiteSpace(baseName) ? "Imported Connection" : baseName.Trim();
		string candidate = normalizedBaseName;
		int index = 1;
		while (names.Contains(candidate))
		{
			// 保留原名，后缀用中文，导入后的列表里更容易看出来。
			candidate = $"{normalizedBaseName}_导入{index++}";
		}

		return candidate;
	}

	private static bool IsAction(ConnectionImportPreviewItem previewItem, string action)
	{
		return string.Equals(previewItem.ImportAction, action, StringComparison.OrdinalIgnoreCase);
	}
}
