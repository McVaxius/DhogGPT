using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DhogGPT.Models;
using DhogGPT.Services;

namespace DhogGPT.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly string[] DtrModes = { "Text only", "Icon + text", "Icon only" };
    private static readonly string[] CompactChatColorThemes =
    {
        "Soft contrast",
        "High contrast",
        "Role tints",
        "Neon night",
        "Custom",
    };

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
        DrawDtrSettings();
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
        if (ImGui.SmallButton("Ko-fi##Settings"))
            Process.Start(new ProcessStartInfo { FileName = Plugin.SupportUrl, UseShellExecute = true });

        changed |= DrawCheckbox("Plugin enabled", configuration.PluginEnabled, value => configuration.PluginEnabled = value);
        changed |= DrawCheckbox("Translate incoming chat", configuration.TranslateIncoming, value => configuration.TranslateIncoming = value);
        changed |= DrawCheckbox("Skip messages from my own character", configuration.SkipOwnMessages, value => configuration.SkipOwnMessages = value);
        changed |= DrawCheckbox("Include sender name in translated Echo output", configuration.IncludeSenderName, value => configuration.IncludeSenderName = value);
        changed |= DrawCheckbox("Include channel label in translated Echo output", configuration.IncludeChannelLabel, value => configuration.IncludeChannelLabel = value);
        changed |= DrawCheckbox("Use simple all-in-one chat mode", configuration.UseSimpleChatMode, value => configuration.UseSimpleChatMode = value);
        changed |= DrawCheckbox("Use compact simple chat header", configuration.CompactSimpleChatMode, value => configuration.CompactSimpleChatMode = value);
        changed |= DrawCheckbox("Krangle names in chat UI", configuration.KrangleChatNames, value => configuration.KrangleChatNames = value);
        changed |= DrawCheckbox("Enable debug logging", configuration.EnableDebugLogging, value => configuration.EnableDebugLogging = value);

        changed |= DrawLanguageCombo("Incoming source language", configuration.IncomingSourceLanguage, value => configuration.IncomingSourceLanguage = value, includeAuto: true);
        changed |= DrawLanguageCombo("Incoming target language", configuration.IncomingTargetLanguage, value => configuration.IncomingTargetLanguage = value, includeAuto: false);

        var windowOpacity = Math.Clamp(configuration.WindowOpacity, 0.35f, 1.0f);
        if (ImGui.SliderFloat("Window opacity", ref windowOpacity, 0.35f, 1.0f, "%.2f"))
        {
            configuration.WindowOpacity = windowOpacity;
            changed = true;
        }

        var compactChatColorTheme = Math.Clamp(configuration.CompactChatColorTheme, 0, CompactChatColorThemes.Length - 1);
        if (ImGui.Combo("Compact chat color theme", ref compactChatColorTheme, CompactChatColorThemes, CompactChatColorThemes.Length))
        {
            configuration.CompactChatColorTheme = compactChatColorTheme;
            changed = true;
        }

        if (configuration.CompactChatColorTheme == CompactChatColorThemes.Length - 1)
        {
            changed |= DrawCustomColorEditor(configuration.CompactChatCustomColors);
        }

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

        ImGui.TextDisabled("Chat logs are stored per account and character under the plugin config Data\\ChatLogs folder.");
        ImGui.TextDisabled("Compact simple chat hides the extra utility strip, while opacity gives the chat window a softer overlay look.");
        ImGui.TextDisabled("Color themes are configured here only, not from the main chat window.");
    }

    private void DrawDtrSettings()
    {
        var configuration = plugin.Configuration;
        var changed = false;

        ImGui.TextUnformatted("DTR bar");
        if (ImGui.Button("Open first-use guide"))
            plugin.OpenFirstUseGuide();

        var dtrBarEnabled = configuration.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR bar entry", ref dtrBarEnabled))
        {
            configuration.DtrBarEnabled = dtrBarEnabled;
            changed = true;
        }

        var dtrMode = configuration.DtrBarMode;
        if (ImGui.Combo("DTR mode", ref dtrMode, DtrModes, DtrModes.Length))
        {
            configuration.DtrBarMode = dtrMode;
            changed = true;
        }

        var enabledGlyph = configuration.DtrIconEnabled;
        if (ImGui.InputText("DTR enabled glyph", ref enabledGlyph, 8))
        {
            configuration.DtrIconEnabled = enabledGlyph.Length <= 3 ? enabledGlyph : enabledGlyph[..3];
            changed = true;
        }

        var disabledGlyph = configuration.DtrIconDisabled;
        if (ImGui.InputText("DTR disabled glyph", ref disabledGlyph, 8))
        {
            configuration.DtrIconDisabled = disabledGlyph.Length <= 3 ? disabledGlyph : disabledGlyph[..3];
            changed = true;
        }

        if (changed)
        {
            configuration.Save();
            plugin.UpdateDtrBar();
        }
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

    private static bool DrawCustomColorEditor(CompactChatCustomColors colors)
    {
        var changed = false;
        ImGui.Separator();
        ImGui.TextUnformatted("Custom compact chat colors");
        changed |= DrawColorPicker("Inbound header", colors.GetInboundHeader(), colors.SetInboundHeader);
        changed |= DrawColorPicker("Inbound translation", colors.GetInboundTranslation(), colors.SetInboundTranslation);
        changed |= DrawColorPicker("Outbound header", colors.GetOutboundHeader(), colors.SetOutboundHeader);
        changed |= DrawColorPicker("Outbound translation", colors.GetOutboundTranslation(), colors.SetOutboundTranslation);
        changed |= DrawColorPicker("Error", colors.GetError(), colors.SetError);
        return changed;
    }

    private static bool DrawColorPicker(string label, Vector4 currentValue, Action<Vector4> setter)
    {
        var color = currentValue;
        if (!ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.AlphaBar))
            return false;

        setter(color);
        return true;
    }
}
