using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DhogGPT.Services;

namespace DhogGPT.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly LanguageRegistryService languageRegistry;

    public ConfigWindow(Plugin plugin, LanguageRegistryService languageRegistry)
        : base("DhogGPT Settings###DhogGPTConfig")
    {
        this.plugin = plugin;
        this.languageRegistry = languageRegistry;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500f, 420f),
            MaximumSize = new Vector2(1100f, 900f),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawGeneralSettings();
        ImGui.Separator();
        DrawIncomingChannelSettings();
        ImGui.Separator();
        DrawProviderSettings();
    }

    private void DrawGeneralSettings()
    {
        var changed = false;
        var configuration = plugin.Configuration;

        ImGui.TextUnformatted("General");
        changed |= DrawCheckbox("Plugin enabled", configuration.PluginEnabled, value => configuration.PluginEnabled = value);
        changed |= DrawCheckbox("Translate incoming chat", configuration.TranslateIncoming, value => configuration.TranslateIncoming = value);
        changed |= DrawCheckbox("Skip messages from my own character", configuration.SkipOwnMessages, value => configuration.SkipOwnMessages = value);
        changed |= DrawCheckbox("Include sender name in translated Echo output", configuration.IncludeSenderName, value => configuration.IncludeSenderName = value);
        changed |= DrawCheckbox("Include channel label in translated Echo output", configuration.IncludeChannelLabel, value => configuration.IncludeChannelLabel = value);
        changed |= DrawCheckbox("Enable debug logging", configuration.EnableDebugLogging, value => configuration.EnableDebugLogging = value);

        changed |= DrawLanguageCombo("Incoming source language", configuration.IncomingSourceLanguage, value => configuration.IncomingSourceLanguage = value, includeAuto: true);
        changed |= DrawLanguageCombo("Incoming target language", configuration.IncomingTargetLanguage, value => configuration.IncomingTargetLanguage = value, includeAuto: false);

        var requestTimeoutSeconds = configuration.RequestTimeoutSeconds;
        if (ImGui.SliderInt("Request timeout seconds", ref requestTimeoutSeconds, 5, 60))
        {
            configuration.RequestTimeoutSeconds = requestTimeoutSeconds;
            changed = true;
        }

        var historyLimit = configuration.HistoryLimit;
        if (ImGui.SliderInt("History entries to keep", ref historyLimit, 5, 200))
        {
            configuration.HistoryLimit = historyLimit;
            changed = true;
        }

        if (changed)
            configuration.Save();
    }

    private void DrawIncomingChannelSettings()
    {
        var changed = false;
        var configuration = plugin.Configuration;

        ImGui.TextUnformatted("Incoming channel filters");
        changed |= DrawCheckbox("Party", configuration.EnableParty, value => configuration.EnableParty = value);
        changed |= DrawCheckbox("Free company", configuration.EnableFreeCompany, value => configuration.EnableFreeCompany = value);
        changed |= DrawCheckbox("Linkshells", configuration.EnableLinkshells, value => configuration.EnableLinkshells = value);
        changed |= DrawCheckbox("Cross-world linkshells", configuration.EnableCrossWorldLinkshells, value => configuration.EnableCrossWorldLinkshells = value);
        changed |= DrawCheckbox("Say", configuration.EnableSay, value => configuration.EnableSay = value);
        changed |= DrawCheckbox("Shout", configuration.EnableShout, value => configuration.EnableShout = value);
        changed |= DrawCheckbox("Yell", configuration.EnableYell, value => configuration.EnableYell = value);
        changed |= DrawCheckbox("DM / tell", configuration.EnableTell, value => configuration.EnableTell = value);

        if (changed)
            configuration.Save();
    }

    private void DrawProviderSettings()
    {
        var changed = false;
        var configuration = plugin.Configuration;

        ImGui.TextUnformatted("Translation provider");
        ImGui.TextWrapped("DhogGPT tries a Google-style no-key web translation endpoint first. If that path fails, it falls back to the LibreTranslate-compatible endpoints listed below, one per line.");

        var providerEndpoints = configuration.ProviderEndpoints;
        if (ImGui.InputTextMultiline("Endpoints", ref providerEndpoints, 4000, new Vector2(-1f, 110f)))
        {
            configuration.ProviderEndpoints = providerEndpoints;
            changed = true;
        }

        if (ImGui.Button("Reset fallback endpoints"))
        {
            configuration.ProviderEndpoints =
                "https://translate.argosopentech.com" + Environment.NewLine +
                "https://libretranslate.de";
            changed = true;
        }

        if (changed)
            configuration.Save();
    }

    private static bool DrawCheckbox(string label, bool value, Action<bool> setter)
    {
        var localValue = value;
        if (!ImGui.Checkbox(label, ref localValue))
            return false;

        setter(localValue);
        return true;
    }

    private bool DrawLanguageCombo(string label, string currentCode, Action<string> setter, bool includeAuto)
    {
        var changed = false;
        var options = includeAuto ? languageRegistry.GetSourceLanguages() : languageRegistry.GetTargetLanguages();
        var displayName = languageRegistry.GetName(currentCode);

        if (ImGui.BeginCombo(label, displayName))
        {
            foreach (var option in options)
            {
                var isSelected = option.Code.Equals(currentCode, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(option.Name, isSelected))
                {
                    setter(option.Code);
                    changed = true;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        return changed;
    }
}
