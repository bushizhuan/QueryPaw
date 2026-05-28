using System;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class ConnectionProfileFilter
{
    public static bool Matches(
        ConnectionProfile profile,
        UiTextSet uiText,
        string selectedEnvironment,
        string selectedGroup,
        string selectedCapability,
        string searchText,
        bool favoritesOnly)
    {
        if (favoritesOnly && !profile.IsFavorite)
        {
            return false;
        }

        if (!MatchesFilter(selectedEnvironment, uiText.AllEnvironments, profile.EnvironmentTag) ||
            !MatchesFilter(selectedGroup, uiText.AllGroups, profile.GroupName) ||
            !MatchesFilter(selectedCapability, uiText.AllCapabilities, profile.CapabilityLevel))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        string value = searchText.Trim();
        return Contains(profile.Name, value) ||
            Contains(profile.Server, value) ||
            Contains(profile.Database, value) ||
            Contains(profile.GroupName, value) ||
            Contains(profile.EnvironmentTag, value) ||
            Contains(profile.ProviderName, value);
    }

    private static bool MatchesFilter(string selectedValue, string allValue, string profileValue)
    {
        return string.IsNullOrWhiteSpace(selectedValue) ||
            string.Equals(selectedValue, allValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profileValue, selectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string? source, string value)
    {
        return source?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
