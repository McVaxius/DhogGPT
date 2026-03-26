using System.Text.Json;
using DhogGPT.Models;

namespace DhogGPT.Services;

public sealed class LanguageRegistryService
{
    private static readonly LanguageOption AutoDetect = new()
    {
        Code = "auto",
        Name = "Autodetect",
    };

    private readonly IReadOnlyList<LanguageOption> baseLanguages;
    private readonly IReadOnlyList<LanguageOption> sourceLanguages;

    public LanguageRegistryService()
    {
        var loaded = LoadLanguages();
        baseLanguages = loaded;
        sourceLanguages = new[] { AutoDetect }.Concat(loaded).ToArray();
    }

    public IReadOnlyList<LanguageOption> GetSourceLanguages() => sourceLanguages;

    public IReadOnlyList<LanguageOption> GetTargetLanguages() => baseLanguages;

    public string GetName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Unknown";

        var normalized = code.Trim().ToLowerInvariant();
        var match = sourceLanguages.FirstOrDefault(language => language.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? normalized.ToUpperInvariant();
    }

    public string NormalizeCode(string? code, bool allowAuto)
    {
        if (string.IsNullOrWhiteSpace(code))
            return allowAuto ? "auto" : "en";

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized == "auto")
            return allowAuto ? "auto" : "en";

        return baseLanguages.Any(language => language.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ? normalized
            : "en";
    }

    private static IReadOnlyList<LanguageOption> LoadLanguages()
    {
        try
        {
            var pluginDir = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
            var path = Path.Combine(pluginDir, "Data", "languages.json");
            if (!File.Exists(path))
                return GetFallbackLanguages();

            var raw = File.ReadAllText(path);
            var options = JsonSerializer.Deserialize<List<LanguageOption>>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (options == null || options.Count == 0)
                return GetFallbackLanguages();

            return options
                .Where(language => !string.IsNullOrWhiteSpace(language.Code) && !string.IsNullOrWhiteSpace(language.Name))
                .Select(language => new LanguageOption
                {
                    Code = language.Code.Trim().ToLowerInvariant(),
                    Name = language.Name.Trim(),
                })
                .OrderBy(language => language.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[DhogGPT] Failed to load languages.json, using fallback list: {ex.Message}");
            return GetFallbackLanguages();
        }
    }

    private static IReadOnlyList<LanguageOption> GetFallbackLanguages()
    {
        return
        [
            new LanguageOption { Code = "ar", Name = "Arabic" },
            new LanguageOption { Code = "bg", Name = "Bulgarian" },
            new LanguageOption { Code = "cs", Name = "Czech" },
            new LanguageOption { Code = "da", Name = "Danish" },
            new LanguageOption { Code = "de", Name = "German" },
            new LanguageOption { Code = "el", Name = "Greek" },
            new LanguageOption { Code = "en", Name = "English" },
            new LanguageOption { Code = "es", Name = "Spanish" },
            new LanguageOption { Code = "et", Name = "Estonian" },
            new LanguageOption { Code = "fi", Name = "Finnish" },
            new LanguageOption { Code = "fr", Name = "French" },
            new LanguageOption { Code = "hu", Name = "Hungarian" },
            new LanguageOption { Code = "id", Name = "Indonesian" },
            new LanguageOption { Code = "it", Name = "Italian" },
            new LanguageOption { Code = "ja", Name = "Japanese" },
            new LanguageOption { Code = "ko", Name = "Korean" },
            new LanguageOption { Code = "lt", Name = "Lithuanian" },
            new LanguageOption { Code = "lv", Name = "Latvian" },
            new LanguageOption { Code = "nl", Name = "Dutch" },
            new LanguageOption { Code = "no", Name = "Norwegian" },
            new LanguageOption { Code = "pl", Name = "Polish" },
            new LanguageOption { Code = "pt", Name = "Portuguese" },
            new LanguageOption { Code = "ro", Name = "Romanian" },
            new LanguageOption { Code = "ru", Name = "Russian" },
            new LanguageOption { Code = "sk", Name = "Slovak" },
            new LanguageOption { Code = "sl", Name = "Slovenian" },
            new LanguageOption { Code = "sv", Name = "Swedish" },
            new LanguageOption { Code = "tr", Name = "Turkish" },
            new LanguageOption { Code = "uk", Name = "Ukrainian" },
            new LanguageOption { Code = "vi", Name = "Vietnamese" },
            new LanguageOption { Code = "zh", Name = "Chinese" },
        ];
    }
}
