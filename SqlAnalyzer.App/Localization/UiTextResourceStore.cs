using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Localization;

public static class UiTextResourceStore
{
    public const string DefaultLanguageCode = "zh-CN";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Lazy<IReadOnlyList<LanguageOption>> LanguageOptions = new(LoadLanguageOptions);
    private static readonly Dictionary<string, UiTextSet> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<LanguageOption> GetLanguageOptions() => LanguageOptions.Value;

    public static UiTextSet GetDefault() => Get(DefaultLanguageCode);

    public static UiTextSet Get(string? languageCode)
    {
        string resolvedCode = ResolveLanguageCode(languageCode);
        lock (Cache)
        {
            if (!Cache.TryGetValue(resolvedCode, out UiTextSet? textSet))
            {
                textSet = LoadTextSet(resolvedCode);
                Cache[resolvedCode] = textSet;
            }

            return textSet;
        }
    }

    public static IReadOnlyList<UiTextSet> GetAllTextSets()
    {
        return GetLanguageOptions()
            .Select(language => Get(language.Code))
            .ToArray();
    }

    private static string ResolveLanguageCode(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            LanguageOption? option = GetLanguageOptions()
                .FirstOrDefault(item => string.Equals(item.Code, languageCode, StringComparison.OrdinalIgnoreCase));
            if (option != null)
            {
                return option.Code;
            }
        }

        return DefaultLanguageCode;
    }

    private static IReadOnlyList<LanguageOption> LoadLanguageOptions()
    {
        LanguageOption[]? languages = LoadJson<LanguageOption[]>("languages.json");
        if (languages == null || languages.Length == 0)
        {
            throw new InvalidDataException("No UI language options are configured.");
        }

        return languages
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .Select(item => new LanguageOption
            {
                Code = item.Code.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Code.Trim() : item.DisplayName.Trim()
            })
            .ToArray();
    }

    private static UiTextSet LoadTextSet(string languageCode)
    {
        UiTextSet? textSet = LoadJson<UiTextSet>($"{languageCode}.json");
        if (textSet == null)
        {
            throw new InvalidDataException($"UI language file is empty: {languageCode}.json");
        }

        ValidateTextSet(languageCode, textSet);
        return textSet;
    }

    private static T? LoadJson<T>(string fileName)
    {
        using Stream stream = OpenLocalizationStream(fileName);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }

    private static Stream OpenLocalizationStream(string fileName)
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "Localization", fileName);
        if (File.Exists(filePath))
        {
            return File.OpenRead(filePath);
        }

        string resourceName = "Localization." + fileName;
        Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            return stream;
        }

        throw new FileNotFoundException($"UI language file was not found: {fileName}", fileName);
    }

    private static void ValidateTextSet(string languageCode, UiTextSet textSet)
    {
        string[] missingKeys = typeof(UiTextSet)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(string))
            .Where(property => string.IsNullOrEmpty((string?)property.GetValue(textSet)))
            .Select(property => property.Name)
            .ToArray();

        if (missingKeys.Length > 0)
        {
            throw new InvalidDataException($"UI language file {languageCode}.json is missing text keys: {string.Join(", ", missingKeys)}");
        }
    }
}
