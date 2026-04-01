using System.Diagnostics;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DhogGPT.Models;
using DhogGPT.Services;

namespace DhogGPT.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private const float WindowRepairTolerance = 4f;
    private static readonly string[] DtrModes = { "Text only", "Icon + text", "Icon only" };
    private static readonly string[] ScrollIndicatorStyles = { "Centered wedges", "Legacy arrows" };
    private static readonly string VersionedWindowTitle = $"DhogGPT Settings v{typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0"}###DhogGPTConfig";
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
    private DateTimeOffset nextWindowPositionSaveUtc = DateTimeOffset.MinValue;
    private Vector2? lastSavedWindowPosition;
    private Vector2 lastObservedWindowSize;
    private Vector2? pendingViewportPlacementPosition;
    private bool pendingSavedPositionApply;
    private bool pendingSizeConditionReset;
    private bool pendingSizeRepair;
    private string pendingViewportPlacementReason = string.Empty;

    public ConfigWindow(Plugin plugin, LanguageRegistryService languageRegistry)
        : base(VersionedWindowTitle)
    {
        this.plugin = plugin;
        this.languageRegistry = languageRegistry;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560f, 460f),
            MaximumSize = new Vector2(1100f, 900f),
        };
        Size = new Vector2(900f, 550f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        ApplyPendingViewportPlacement();
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("DhogGPTSettingsTabs"))
        {
            if (ImGui.BeginTabItem("Everyday"))
            {
                DrawEverydaySettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Channels"))
            {
                DrawIncomingChannelSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Providers"))
            {
                DrawProviderSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Advanced"))
            {
                DrawAdvancedSettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        TrackWindowPosition();
    }

    public void ApplySavedPositionForCurrentCharacter()
    {
        if (plugin.TryGetSavedWindowPosition(true, out var position))
        {
            if (plugin.Configuration.KeepWindowsOnCurrentGameScreen)
            {
                QueueViewportPlacement(position.ToVector2(), "saved settings-window restore");
                return;
            }

            Position = position.ToVector2();
            PositionCondition = ImGuiCond.Always;
            pendingSavedPositionApply = true;
            return;
        }

        if (plugin.Configuration.KeepWindowsOnCurrentGameScreen)
        {
            QueueViewportPlacement(new Vector2(1f, 1f), "fallback settings-window restore", forceSizeRepair: true);
            return;
        }

        Position = new Vector2(1f, 1f);
        PositionCondition = ImGuiCond.Always;
        pendingSavedPositionApply = true;
    }

    private void DrawEverydaySettings()
    {
        var configuration = plugin.Configuration;
        var changed = false;

        ImGui.TextUnformatted("Common DhogGPT settings");
        if (ImGui.SmallButton("Ko-fi##Settings"))
            Process.Start(new ProcessStartInfo { FileName = Plugin.SupportUrl, UseShellExecute = true });
        DrawTooltipOnLastItem("Open the DhogGPT support page.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Discord##Settings"))
            Process.Start(new ProcessStartInfo { FileName = Plugin.DiscordUrl, UseShellExecute = true });
        DrawTooltipOnLastItem("Open the DhogGPT Discord server.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Guide##Settings"))
            plugin.OpenFirstUseGuide();
        DrawTooltipOnLastItem("Open the first-use guide again.");

        var ultraCompactMode = plugin.IsUltraCompactModeConfigured();
        if (ImGui.Checkbox("Ultra compact mode", ref ultraCompactMode))
            plugin.SetUltraCompactMode(ultraCompactMode);
        DrawTooltipOnLastItem("Switch between regular mode and DhogGPT's supported ultra compact chat surface.");

        changed |= DrawCheckbox(
            "Disable vanilla chat window while Ultra Compact is on",
            configuration.SuppressVanillaChatWindow,
            value => configuration.SuppressVanillaChatWindow = value,
            "Keeps the game's vanilla chat hidden while DhogGPT ultra compact mode is active. Leave this on if you want DhogGPT to fully take over; turn it off if you want both chat windows visible.");

        changed |= DrawCheckbox(
            "Plugin enabled",
            configuration.PluginEnabled,
            value => configuration.PluginEnabled = value,
            "Master DhogGPT enable switch. Incoming translation and related automation stop when this is off.");
        changed |= DrawCheckbox(
            "Translate incoming chat",
            configuration.TranslateIncoming,
            value => configuration.TranslateIncoming = value,
            "Translate allowed incoming chat channels into your chosen target language.");
        changed |= DrawCheckbox(
            "Open main window when a DM arrives",
            configuration.OpenMainWindowOnIncomingDirectMessage,
            value => configuration.OpenMainWindowOnIncomingDirectMessage = value,
            "Brings DhogGPT to the front when a new incoming tell is observed.");
        changed |= DrawCheckbox(
            "Open main window when a character loads",
            configuration.OpenMainWindowOnCharacterLogin,
            value => configuration.OpenMainWindowOnCharacterLogin = value,
            "Reopens the DhogGPT main window automatically on character login.");
        changed |= DrawCheckbox(
            "Keep windows on current game screen",
            configuration.KeepWindowsOnCurrentGameScreen,
            value => configuration.KeepWindowsOnCurrentGameScreen = value,
            "When enabled, DhogGPT clamps saved window restores and automatic window repairs to the active game viewport. Leave this off if you prefer manual recovery with /dgpt j.");
        changed |= DrawCheckbox(
            "Ultra compact: focus chat on /",
            configuration.FocusUltraCompactOnSlash,
            value => configuration.FocusUltraCompactOnSlash = value,
            "When ultra compact mode is active and the DhogGPT window is open but unfocused, pressing / focuses DhogGPT and starts a slash command immediately.");
        changed |= DrawCheckbox(
            "Ultra compact: focus chat on Enter",
            configuration.FocusUltraCompactOnEnter,
            value => configuration.FocusUltraCompactOnEnter = value,
            "When ultra compact mode is active and the DhogGPT window is open but unfocused, pressing Enter focuses the DhogGPT composer.");
        changed |= DrawCheckbox(
            "Krangle names in chat UI",
            configuration.KrangleChatNames,
            value => configuration.KrangleChatNames = value,
            "Applies display-only Krangling to names inside the DhogGPT UI without changing the real game chat.");

        changed |= DrawLanguageCombo(
            "Incoming source language",
            configuration.IncomingSourceLanguage,
            value => configuration.IncomingSourceLanguage = value,
            includeAuto: true,
            "Language assumption for incoming messages. Leave this on Auto unless you know the source language ahead of time.");
        changed |= DrawLanguageCombo(
            "Incoming target language",
            configuration.IncomingTargetLanguage,
            value => configuration.IncomingTargetLanguage = value,
            includeAuto: false,
            "Language that incoming translations should be rendered into.");

        var focusedOpacity = Math.Clamp(configuration.FocusedWindowOpacity, 0.20f, 1.0f);
        if (ImGui.SliderFloat("Focused or hovered opacity", ref focusedOpacity, 0.20f, 1.0f, "%.2f"))
        {
            configuration.FocusedWindowOpacity = focusedOpacity;
            changed = true;
        }
        DrawTooltipOnLastItem("Opacity while the DhogGPT main window is hovered or focused.");

        var backgroundOpacity = Math.Clamp(configuration.BackgroundWindowOpacity, 0.20f, 1.0f);
        if (ImGui.SliderFloat("Background opacity", ref backgroundOpacity, 0.20f, 1.0f, "%.2f"))
        {
            configuration.BackgroundWindowOpacity = backgroundOpacity;
            changed = true;
        }
        DrawTooltipOnLastItem("Opacity after you click away from the DhogGPT main window.");

        var fadedComposerOpacity = Math.Clamp(configuration.WindowOpacity, 0.20f, 1.0f);
        if (ImGui.SliderFloat("Faded chatbox edit opacity", ref fadedComposerOpacity, 0.20f, 1.0f, "%.2f"))
        {
            configuration.WindowOpacity = fadedComposerOpacity;
            changed = true;
        }
        DrawTooltipOnLastItem("Opacity of the bottom chatbox entry area when DhogGPT is faded and you are not actively typing. While you are typing, the composer stays fully opaque.");

        var compactChatColorTheme = Math.Clamp(configuration.CompactChatColorTheme, 0, CompactChatColorThemes.Length - 1);
        if (ImGui.Combo("Ultra compact chat color theme", ref compactChatColorTheme, CompactChatColorThemes, CompactChatColorThemes.Length))
        {
            configuration.CompactChatColorTheme = compactChatColorTheme;
            changed = true;
        }
        DrawTooltipOnLastItem("Controls ultra compact message colors and the conversation tab palette.");

        changed |= DrawCheckbox(
            "Ultra compact: use real-chat channel header colors",
            configuration.UseRealChatColorParity,
            value => configuration.UseRealChatColorParity = value,
            "Uses vanilla-style channel colors for message header lines like Party, FC, LS, DM, and Shout while keeping the selected translation/theme colors for translated text.");

        var scrollIndicatorStyle = Math.Clamp(configuration.ScrollIndicatorStyle, 0, ScrollIndicatorStyles.Length - 1);
        if (ImGui.Combo("Chat scroll indicator style", ref scrollIndicatorStyle, ScrollIndicatorStyles, ScrollIndicatorStyles.Length))
        {
            configuration.ScrollIndicatorStyle = scrollIndicatorStyle;
            changed = true;
        }
        DrawTooltipOnLastItem("Choose the default look for hidden-scroll hints in the conversation body.");

        if (configuration.CompactChatColorTheme == CompactChatColorThemes.Length - 1)
            changed |= DrawCustomColorEditor(configuration.CompactChatCustomColors);

        if (changed)
            configuration.Save();

        ImGui.Separator();
        ImGui.TextDisabled("Compact and Super Compact are deprecated. DhogGPT now supports Regular mode and Ultra compact mode.");
        ImGui.TextDisabled("The / and Enter focus shortcuts only apply while the ultra compact window is already open.");
        ImGui.TextDisabled("Slash commands sent through DhogGPT skip translation and JSONL logging, but they still leave a Safe breadcrumb.");
    }

    private void DrawIncomingChannelSettings()
    {
        var changed = false;
        var configuration = plugin.Configuration;

        ImGui.TextUnformatted("Incoming and channel behavior");
        changed |= DrawCheckbox(
            "Skip messages from my own character",
            configuration.SkipOwnMessages,
            value => configuration.SkipOwnMessages = value,
            "Prevents DhogGPT from translating messages that appear to come from your own character.");
        changed |= DrawCheckbox(
            "Include sender name in translated Echo output",
            configuration.IncludeSenderName,
            value => configuration.IncludeSenderName = value,
            "Adds the sender identity into DhogGPT's Echo mirror for translated incoming chat.");
        changed |= DrawCheckbox(
            "Include channel label in translated Echo output",
            configuration.IncludeChannelLabel,
            value => configuration.IncludeChannelLabel = value,
            "Adds the channel label such as Party, FC, or Tell into DhogGPT's Echo mirror output.");
        changed |= DrawCheckbox("Party", configuration.EnableParty, value => configuration.EnableParty = value, "Enable translation for Party, Cross-party, and Alliance chat.");
        changed |= DrawCheckbox("PvP team", configuration.EnablePvPTeam, value => configuration.EnablePvPTeam = value, "Enable translation for PvP Team chat.");
        changed |= DrawCheckbox("Free company", configuration.EnableFreeCompany, value => configuration.EnableFreeCompany = value, "Enable translation for Free Company chat.");
        changed |= DrawCheckbox("Linkshells", configuration.EnableLinkshells, value => configuration.EnableLinkshells = value, "Enable translation for linkshell channels LS1-LS8.");
        changed |= DrawCheckbox("Cross-world linkshells", configuration.EnableCrossWorldLinkshells, value => configuration.EnableCrossWorldLinkshells = value, "Enable translation for cross-world linkshell channels CWLS1-CWLS8.");
        changed |= DrawCheckbox("Say", configuration.EnableSay, value => configuration.EnableSay = value, "Enable translation for Say chat.");
        changed |= DrawCheckbox("Shout", configuration.EnableShout, value => configuration.EnableShout = value, "Enable translation for Shout chat.");
        changed |= DrawCheckbox("Yell", configuration.EnableYell, value => configuration.EnableYell = value, "Enable translation for Yell chat.");
        changed |= DrawCheckbox("NN", configuration.EnableNoviceNetwork, value => configuration.EnableNoviceNetwork = value, "Enable translation for Novice Network chat.");
        changed |= DrawCheckbox("DM / tell", configuration.EnableTell, value => configuration.EnableTell = value, "Enable translation for incoming and outgoing tells.");

        if (changed)
            configuration.Save();

        ImGui.Separator();
        ImGui.TextDisabled("Safe, Echo, Progress, and Combat are DhogGPT-owned channels. Their visibility is controlled from the main chat window instead of here.");
    }

    private void DrawProviderSettings()
    {
        var changed = false;
        var configuration = plugin.Configuration;

        ImGui.TextUnformatted("Translation provider and timeout");
        ImGui.TextWrapped("DhogGPT tries a Google-style no-key web translation endpoint first. If that path fails, it falls back to the LibreTranslate-compatible endpoints listed below, one per line.");

        var requestTimeoutSeconds = configuration.RequestTimeoutSeconds;
        if (ImGui.SliderInt("Request timeout seconds", ref requestTimeoutSeconds, 5, 60))
        {
            configuration.RequestTimeoutSeconds = requestTimeoutSeconds;
            changed = true;
        }
        DrawTooltipOnLastItem("Maximum time DhogGPT waits for a single translation request before treating it as failed.");

        var providerEndpoints = configuration.ProviderEndpoints;
        if (ImGui.InputTextMultiline("Endpoints", ref providerEndpoints, 4000, new Vector2(-1f, 140f)))
        {
            configuration.ProviderEndpoints = providerEndpoints;
            changed = true;
        }
        DrawTooltipOnLastItem("Fallback LibreTranslate-style endpoints, one per line, used when the primary web route is unavailable.");

        if (ImGui.Button("Reset fallback endpoints"))
        {
            configuration.ProviderEndpoints =
                "https://translate.argosopentech.com" + Environment.NewLine +
                "https://libretranslate.de";
            changed = true;
        }
        DrawTooltipOnLastItem("Restore the default fallback translation endpoints.");

        if (changed)
            configuration.Save();
    }

    private void DrawAdvancedSettings()
    {
        var configuration = plugin.Configuration;
        var changed = false;

        ImGui.TextUnformatted("Advanced and diagnostics");
        if (ImGui.Button("Open first-use guide"))
            plugin.OpenFirstUseGuide();
        DrawTooltipOnLastItem("Reopen the first-use guide window.");

        var dtrBarEnabled = configuration.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR bar entry", ref dtrBarEnabled))
        {
            configuration.DtrBarEnabled = dtrBarEnabled;
            changed = true;
        }
        DrawTooltipOnLastItem("Show or hide the DhogGPT entry in the Dalamud DTR bar.");

        var dtrMode = configuration.DtrBarMode;
        if (ImGui.Combo("DTR mode", ref dtrMode, DtrModes, DtrModes.Length))
        {
            configuration.DtrBarMode = dtrMode;
            changed = true;
        }
        DrawTooltipOnLastItem("Choose whether the DTR entry shows text, icon plus text, or icon only.");

        var enabledGlyph = configuration.DtrIconEnabled;
        if (ImGui.InputText("DTR enabled glyph", ref enabledGlyph, 8))
        {
            configuration.DtrIconEnabled = enabledGlyph.Length <= 3 ? enabledGlyph : enabledGlyph[..3];
            changed = true;
        }
        DrawTooltipOnLastItem("Glyph used for the DTR entry while DhogGPT is enabled.");

        var disabledGlyph = configuration.DtrIconDisabled;
        if (ImGui.InputText("DTR disabled glyph", ref disabledGlyph, 8))
        {
            configuration.DtrIconDisabled = disabledGlyph.Length <= 3 ? disabledGlyph : disabledGlyph[..3];
            changed = true;
        }
        DrawTooltipOnLastItem("Glyph used for the DTR entry while DhogGPT is disabled.");

        changed |= DrawCheckbox(
            "Lock main window position by default",
            configuration.LockMainWindowPosition,
            value => configuration.LockMainWindowPosition = value,
            "Same behavior as the main-window titlebar lock button. Useful if you almost always want DhogGPT pinned in place.");
        changed |= DrawCheckbox(
            "Enable debug logging",
            configuration.EnableDebugLogging,
            value => configuration.EnableDebugLogging = value,
            "Turns on extra DhogGPT diagnostics. Keep this off unless you are investigating a problem.");

        var historyLimit = configuration.HistoryLimit;
        if (ImGui.SliderInt("History entries to keep", ref historyLimit, 5, 200))
        {
            configuration.HistoryLimit = historyLimit;
            changed = true;
        }
        DrawTooltipOnLastItem("Maximum number of translated history items DhogGPT keeps in memory per loaded chat log.");

        if (changed)
        {
            configuration.Save();
            plugin.UpdateDtrBar();
        }

        if (ImGui.SmallButton("Open chat log folder"))
        {
            var logDirectory = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "Data", "ChatLogs");
            Directory.CreateDirectory(logDirectory);
            Process.Start(new ProcessStartInfo { FileName = logDirectory, UseShellExecute = true });
        }
        DrawTooltipOnLastItem("Open DhogGPT's per-character chat-log folder on disk.");

        ImGui.TextDisabled("Chat logs are stored per account and character under the plugin config Data\\ChatLogs folder.");
        ImGui.TextDisabled(Plugin.DiscordFeedbackNote);
    }

    private void TrackWindowPosition()
    {
        var currentPosition = ImGui.GetWindowPos();
        var currentSize = ImGui.GetWindowSize();
        lastObservedWindowSize = currentSize;
        if (pendingSavedPositionApply)
        {
            pendingSavedPositionApply = false;
            Position = null;
            PositionCondition = ImGuiCond.None;
        }

        if (pendingSizeConditionReset)
        {
            pendingSizeConditionReset = false;
            SizeCondition = ImGuiCond.None;
        }

        if (ImGui.IsWindowAppearing())
            LogWindowSnapshot("appearing", currentPosition, currentSize);

        if (TryQueueWindowRepair(currentPosition, currentSize))
            return;

        if (DateTimeOffset.UtcNow < nextWindowPositionSaveUtc)
            return;

        if (lastSavedWindowPosition.HasValue &&
            Vector2.DistanceSquared(lastSavedWindowPosition.Value, currentPosition) < 0.25f)
        {
            return;
        }

        lastSavedWindowPosition = currentPosition;
        nextWindowPositionSaveUtc = DateTimeOffset.UtcNow.AddMilliseconds(250);
        plugin.SaveCurrentWindowPosition(true, currentPosition);
    }

    private void QueueViewportPlacement(Vector2? requestedPosition, string reason, bool forceSizeRepair = false)
    {
        pendingViewportPlacementPosition = requestedPosition;
        pendingViewportPlacementReason = reason;
        pendingSizeRepair |= forceSizeRepair;
    }

    private void ApplyPendingViewportPlacement()
    {
        if (!pendingViewportPlacementPosition.HasValue)
            return;

        var viewport = ImGui.GetMainViewport();
        var minimumSize = new Vector2(560f, 460f);
        var preferredSize =
            !pendingSizeRepair &&
            lastObservedWindowSize.X >= minimumSize.X - WindowRepairTolerance &&
            lastObservedWindowSize.Y >= minimumSize.Y - WindowRepairTolerance
                ? lastObservedWindowSize
                : new Vector2(900f, 550f);
        var windowSize = WindowPlacementHelper.GetSafeWindowSize(minimumSize, preferredSize, viewport.WorkSize);
        var desiredPosition = pendingViewportPlacementPosition.Value;
        var appliedPosition = WindowPlacementHelper.ClampToWorkArea(desiredPosition, windowSize, viewport.WorkPos, viewport.WorkSize);
        var clamped = Vector2.DistanceSquared(desiredPosition, appliedPosition) >= 0.25f;
        var reason = pendingViewportPlacementReason;
        var forcedSizeRepair = pendingSizeRepair;

        Position = appliedPosition;
        PositionCondition = ImGuiCond.Always;
        pendingSavedPositionApply = true;

        if (forcedSizeRepair)
        {
            Size = windowSize;
            SizeCondition = ImGuiCond.Always;
            pendingSizeConditionReset = true;
        }

        Plugin.Log.Information(
            $"[DhogGPT] Settings window placement applied: reason={reason}, " +
            $"desired={FormatVector2(desiredPosition)}, applied={FormatVector2(appliedPosition)}, " +
            $"windowSize={FormatVector2(windowSize)}, viewportWorkPos={FormatVector2(viewport.WorkPos)}, " +
            $"viewportWorkSize={FormatVector2(viewport.WorkSize)}, clamped={clamped}, forceSizeRepair={forcedSizeRepair}");

        pendingViewportPlacementPosition = null;
        pendingViewportPlacementReason = string.Empty;
        pendingSizeRepair = false;
    }

    private bool TryQueueWindowRepair(Vector2 currentPosition, Vector2 currentSize)
    {
        if (!plugin.Configuration.KeepWindowsOnCurrentGameScreen)
            return false;

        if (pendingViewportPlacementPosition.HasValue)
            return true;

        var minimumSize = new Vector2(560f, 460f);
        var tooSmall =
            currentSize.X < minimumSize.X - WindowRepairTolerance ||
            currentSize.Y < minimumSize.Y - WindowRepairTolerance;
        var viewport = ImGui.GetMainViewport();
        var offscreen = !WindowPlacementHelper.IsInsideWorkArea(currentPosition, currentSize, viewport.WorkPos, viewport.WorkSize);
        if (!tooSmall && !offscreen)
            return false;

        var reason = offscreen
            ? $"detected off-screen settings window at {FormatVector2(currentPosition)}"
            : $"detected undersized settings window at {FormatVector2(currentSize)}";
        QueueViewportPlacement(currentPosition, reason, forceSizeRepair: true);
        Plugin.Log.Warning(
            $"[DhogGPT] Settings window repair queued: reason={reason}, " +
            $"currentPos={FormatVector2(currentPosition)}, currentSize={FormatVector2(currentSize)}, " +
            $"viewportWorkPos={FormatVector2(viewport.WorkPos)}, viewportWorkSize={FormatVector2(viewport.WorkSize)}");
        return true;
    }

    private static void LogWindowSnapshot(string reason, Vector2 currentPosition, Vector2 currentSize)
    {
        var viewport = ImGui.GetMainViewport();
        Plugin.Log.Information(
            $"[DhogGPT] Settings window snapshot: reason={reason}, " +
            $"pos={FormatVector2(currentPosition)}, size={FormatVector2(currentSize)}, " +
            $"collapsed={ImGui.IsWindowCollapsed()}, viewportWorkPos={FormatVector2(viewport.WorkPos)}, " +
            $"viewportWorkSize={FormatVector2(viewport.WorkSize)}");
    }

    private static string FormatVector2(Vector2 value)
        => $"{value.X:F1},{value.Y:F1}";

    private static bool DrawCheckbox(string label, bool value, Action<bool> setter, string tooltip)
    {
        var localValue = value;
        var changed = ImGui.Checkbox(label, ref localValue);
        DrawTooltipOnLastItem(tooltip);
        if (!changed)
            return false;

        setter(localValue);
        return true;
    }

    private bool DrawLanguageCombo(string label, string currentCode, Action<string> setter, bool includeAuto, string tooltip)
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
        DrawTooltipOnLastItem(tooltip);

        return changed;
    }

    private static bool DrawCustomColorEditor(CompactChatCustomColors colors)
    {
        var changed = false;
        ImGui.Separator();
        ImGui.TextUnformatted("Custom ultra compact message and tab colors");
        ImGui.Spacing();
        ImGui.TextUnformatted("Messages");
        changed |= DrawColorPicker("Inbound header", colors.GetInboundHeader(), colors.SetInboundHeader, "Color used for inbound message header lines.");
        changed |= DrawColorPicker("Inbound translation", colors.GetInboundTranslation(), colors.SetInboundTranslation, "Color used for inbound translated lines.");
        changed |= DrawColorPicker("Outbound header", colors.GetOutboundHeader(), colors.SetOutboundHeader, "Color used for outbound message header lines.");
        changed |= DrawColorPicker("Outbound translation", colors.GetOutboundTranslation(), colors.SetOutboundTranslation, "Color used for outbound translated lines.");
        changed |= DrawColorPicker("Error", colors.GetError(), colors.SetError, "Color used for translation failure text.");
        ImGui.Spacing();
        ImGui.TextUnformatted("Conversation tabs");
        changed |= DrawColorPicker("Tab", colors.GetTab(), colors.SetTab, "Background color for inactive conversation tabs.");
        changed |= DrawColorPicker("Tab hovered", colors.GetTabHovered(), colors.SetTabHovered, "Background color when hovering a conversation tab.");
        changed |= DrawColorPicker("Tab active", colors.GetTabActive(), colors.SetTabActive, "Background color for the selected conversation tab.");
        changed |= DrawColorPicker("Tab unfocused", colors.GetTabUnfocused(), colors.SetTabUnfocused, "Background color for inactive tabs while the window is unfocused.");
        changed |= DrawColorPicker("Tab unfocused active", colors.GetTabUnfocusedActive(), colors.SetTabUnfocusedActive, "Background color for the selected tab while the window is unfocused.");
        changed |= DrawColorPicker("Tab text", colors.GetTabText(), colors.SetTabText, "Text color used on inactive conversation tabs.");
        changed |= DrawColorPicker("Selected tab text", colors.GetActiveTabText(), colors.SetActiveTabText, "Text color used on the selected tab. Black is best when the active tab background is bright.");
        return changed;
    }

    private static bool DrawColorPicker(string label, Vector4 currentValue, Action<Vector4> setter, string tooltip)
    {
        var color = currentValue;
        var changed = ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.AlphaBar);
        DrawTooltipOnLastItem(tooltip);
        if (!changed)
            return false;

        setter(color);
        return true;
    }

    private static void DrawTooltipOnLastItem(string tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
            return;

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);
    }
}
