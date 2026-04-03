using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DhogGPT.Managers;
using DhogGPT.Models;
using DhogGPT.Services;
using DhogGPT.Services.Chat;
using DhogGPT.Services.Diagnostics;
using DhogGPT.Services.Translation;
using DhogGPT.Services.Translation.Providers;
using DhogGPT.Windows;

namespace DhogGPT;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static ISeStringEvaluator SeStringEvaluator { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/dhoggpt";
    private const string AliasCommandName = "/dgpt";
    private const string ShortAliasCommandName = "/dog";
    public const string DisplayName = "DhogGPT";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";
    public const string DiscordUrl = "https://discord.gg/VsXqydsvpu";
    public const string DiscordFeedbackNote = "Scroll down to \"The Dumpster Fire\" channel to discuss issues / suggestions for specific plugins.";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("DhogGPT");
    public LinkPayloadManager LinkPayloadManager { get; }
    public LanguageRegistryService LanguageRegistry { get; }
    public SessionHealthService SessionHealth { get; }
    public TranslationCacheService TranslationCache { get; }
    public LibreTranslateProvider TranslationProvider { get; }
    public TranslationCoordinator TranslationCoordinator { get; }
    public ChatTranslationService ChatTranslationService { get; }
    public ChatLogService ChatLogService { get; }
    public SupplementalLogChannelService SupplementalLogChannelService { get; }
    public VanillaChatWindowService VanillaChatWindowService { get; }

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly FirstUseGuideWindow firstUseGuideWindow;
    private readonly Dictionary<string, MainWindow> detachedConversationWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> detachedConversationAssignments = new(StringComparer.OrdinalIgnoreCase);
    private IDtrBarEntry? dtrEntry;
    private bool pendingLoginWindowRestore;
    private bool restoreLoginWindowStateWhenCharacterReady = true;
    private bool wasUltraCompactSlashDown;
    private bool wasUltraCompactEnterDown;
    private int nextDetachedWindowNumber = 1;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        NormalizeLegacyConfiguration();
        NormalizeChatModeConfiguration();
        InitializeConversationVisibilityDefaults();

        LinkPayloadManager = new LinkPayloadManager();
        LanguageRegistry = new LanguageRegistryService();
        SessionHealth = new SessionHealthService();
        TranslationCache = new TranslationCacheService();
        TranslationProvider = new LibreTranslateProvider(Configuration);
        TranslationCoordinator = new TranslationCoordinator(Configuration, TranslationCache, TranslationProvider, SessionHealth);
        ChatLogService = new ChatLogService(TranslationCoordinator);
        ChatTranslationService = new ChatTranslationService(Configuration, TranslationCoordinator);
        SupplementalLogChannelService = new SupplementalLogChannelService(ChatLogService);

        mainWindow = new MainWindow(this, LanguageRegistry, TranslationCoordinator, SessionHealth, ChatLogService);
        configWindow = new ConfigWindow(this, LanguageRegistry);
        firstUseGuideWindow = new FirstUseGuideWindow(this);
        VanillaChatWindowService = new VanillaChatWindowService(() => ShouldSuppressVanillaChatWindow() && mainWindow.IsOpen);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(firstUseGuideWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open DhogGPT. Use /dhoggpt config for settings, /dhoggpt ultra for ultra compact mode, /dhoggpt ws to reset window positions, or /dhoggpt j to jump the main window somewhere visible.",
        });

        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /dhoggpt.",
        });

        CommandManager.AddHandler(ShortAliasCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Short alias for /dhoggpt.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;

        SetupDtrBar();
        UpdateDtrBar();
        ContextMenu.OnMenuOpened += OnContextMenuOpened;
        ClientState.Login += OnLogin;
        Framework.Update += OnFrameworkUpdate;
        pendingLoginWindowRestore = false;

        Log.Information("[DhogGPT] Plugin loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(ShortAliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLogin;
        WindowSystem.RemoveAllWindows();

        dtrEntry?.Remove();
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        foreach (var detachedWindow in detachedConversationWindows.Values.ToList())
            detachedWindow.Dispose();

        detachedConversationWindows.Clear();
        detachedConversationAssignments.Clear();
        firstUseGuideWindow.Dispose();
        mainWindow.Dispose();
        configWindow.Dispose();
        LinkPayloadManager.Dispose();
        VanillaChatWindowService.Dispose();
        SupplementalLogChannelService.Dispose();
        ChatLogService.Dispose();
        ChatTranslationService.Dispose();
        TranslationCoordinator.Dispose();
        TranslationProvider.Dispose();

        Log.Information("[DhogGPT] Plugin unloaded.");
    }

    public void ToggleMainUi()
    {
        Log.Information($"[DhogGPT] ToggleMainUi requested: mainOpen={mainWindow.IsOpen}, ultraCompact={IsUltraCompactModeConfigured()}");
        if (!mainWindow.IsOpen)
            mainWindow.ApplySavedPositionForCurrentCharacter();

        mainWindow.Toggle();
    }

    public void OpenMainUi()
    {
        Log.Information($"[DhogGPT] OpenMainUi requested: mainOpen={mainWindow.IsOpen}, ultraCompact={IsUltraCompactModeConfigured()}");
        if (!mainWindow.IsOpen)
            mainWindow.ApplySavedPositionForCurrentCharacter();

        mainWindow.IsOpen = true;
        mainWindow.RequestSimpleComposerFocus();
    }

    public void JumpMainWindowToRandomVisibleLocation()
    {
        Log.Information("[DhogGPT] Queued a random visible jump for the main window via /dgpt j.");
        mainWindow.QueueRandomVisibleJump();
        mainWindow.IsOpen = true;
        mainWindow.RequestSimpleComposerFocus();
        PrintStatus("Queued a random visible jump for the DhogGPT main window.");
    }

    public void ToggleConfigUi()
    {
        if (!configWindow.IsOpen)
            configWindow.ApplySavedPositionForCurrentCharacter();

        configWindow.Toggle();
    }

    public void OpenConfigUi()
    {
        if (!configWindow.IsOpen)
            configWindow.ApplySavedPositionForCurrentCharacter();

        configWindow.IsOpen = true;
    }

    public void OpenFirstUseGuide() => firstUseGuideWindow.IsOpen = true;

    public void PrintStatus(string message)
    {
        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = $"[DhogGPT] {message}",
        });
    }

    public void SetPluginEnabled(bool enabled, bool printStatus = false)
    {
        Configuration.PluginEnabled = enabled;
        Configuration.Save();
        UpdateDtrBar();

        if (printStatus)
            PrintStatus(enabled ? "Plugin enabled." : "Plugin disabled.");
    }

    public bool IsUltraCompactModeConfigured()
        => Configuration.UseSimpleChatMode &&
           Configuration.CompactSimpleChatMode;

    public bool ShouldSuppressVanillaChatWindow()
        => IsUltraCompactModeConfigured() &&
           Configuration.SuppressVanillaChatWindow;

    public void SetUltraCompactMode(bool enabled, bool printStatus = false)
    {
        if (enabled)
            ApplyUltraCompactConfiguration();
        else
            ApplyRegularModeConfiguration();

        Configuration.Save();
        if (enabled)
            OpenMainUi();

        if (printStatus)
            PrintStatus(enabled
                ? "Ultra compact mode enabled."
                : "Ultra compact mode disabled.");
    }

    public void MarkFirstUseGuideSeen()
    {
        if (Configuration.HasSeenFirstUseGuide)
            return;

        Configuration.HasSeenFirstUseGuide = true;
        Configuration.Save();
    }

    private void NormalizeChatModeConfiguration()
    {
        if (IsUltraCompactModeConfigured())
        {
            if (!Configuration.UseSuperCompactLanguageBar)
            {
                Configuration.UseSuperCompactLanguageBar = true;
                Configuration.Save();
            }

            return;
        }

        if (Configuration.UseSimpleChatMode ||
            Configuration.CompactSimpleChatMode ||
            Configuration.UseSuperCompactLanguageBar)
        {
            ApplyUltraCompactConfiguration();
            Configuration.Save();
        }
    }

    private void NormalizeLegacyConfiguration()
    {
        var changed = false;

        if (Configuration.Version < 2)
        {
            if (!Configuration.PluginEnabled && Configuration.TranslateIncoming)
            {
                Configuration.PluginEnabled = true;
                changed = true;
                Log.Information("[DhogGPT] Migrated legacy config: restored PluginEnabled because incoming translation was already configured on.");
            }

            Configuration.Version = 2;
            changed = true;
        }

        if (changed)
            Configuration.Save();
    }

    private void ApplyUltraCompactConfiguration()
    {
        Configuration.UseSimpleChatMode = true;
        Configuration.CompactSimpleChatMode = true;
        Configuration.UseSuperCompactLanguageBar = true;
    }

    private void ApplyRegularModeConfiguration()
    {
        Configuration.UseSimpleChatMode = false;
        Configuration.CompactSimpleChatMode = false;
        Configuration.UseSuperCompactLanguageBar = false;
    }

    private void OnCommand(string command, string arguments)
    {
        var trimmed = arguments.Trim();
        if (trimmed.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            ToggleConfigUi();
            return;
        }

        if (trimmed.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = SessionHealth.GetSnapshot();
            PrintStatus($"Queue={snapshot.QueueDepth}, success={snapshot.SuccessCount}, failure={snapshot.FailureCount}");
            return;
        }

        if (trimmed.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            ResetCurrentWindowPositions();
            return;
        }

        if (trimmed.Equals("j", StringComparison.OrdinalIgnoreCase))
        {
            JumpMainWindowToRandomVisibleLocation();
            return;
        }

        if (trimmed.Equals("ultra", StringComparison.OrdinalIgnoreCase))
        {
            SetUltraCompactMode(!IsUltraCompactModeConfigured(), printStatus: true);
            return;
        }

        if (trimmed.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            SetPluginEnabled(true, printStatus: true);
            return;
        }

        if (trimmed.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            SetPluginEnabled(false, printStatus: true);
            return;
        }

        ToggleMainUi();
    }

    public bool TryGetCurrentCharacterStateKey(out string key)
    {
        var contentId = PlayerState.ContentId;
        if (contentId == 0)
        {
            key = string.Empty;
            return false;
        }

        key = contentId.ToString("X16");
        return true;
    }

    public bool TryGetSavedWindowPosition(bool settingsWindow, out SavedWindowPosition position)
    {
        if (TryGetCurrentCharacterStateKey(out var key) &&
            Configuration.CharacterWindowStates.TryGetValue(key, out var state))
        {
            position = settingsWindow ? state.SettingsWindow : state.MainWindow;
            return position.HasValue;
        }

        position = new SavedWindowPosition();
        return false;
    }

    public void SaveCurrentWindowPosition(bool settingsWindow, Vector2 position)
    {
        if (!TryGetCurrentCharacterStateKey(out var key))
            return;

        var state = GetOrCreateCharacterWindowState(key);
        var savedPosition = settingsWindow ? state.SettingsWindow : state.MainWindow;
        if (savedPosition.HasValue &&
            Vector2.DistanceSquared(savedPosition.ToVector2(), position) < 0.25f)
        {
            return;
        }

        savedPosition.Set(position);
        Configuration.Save();
    }

    public void ResetCurrentWindowPositions()
    {
        if (!TryGetCurrentCharacterStateKey(out var key))
        {
            PrintStatus("Window positions could not be reset because no character is active.");
            return;
        }

        var state = GetOrCreateCharacterWindowState(key);
        state.MainWindow.Reset();
        state.SettingsWindow.Reset();
        Configuration.Save();

        Log.Information($"[DhogGPT] ResetCurrentWindowPositions requested for character {key}: main=1,1 settings=1,1");
        mainWindow.ApplySavedPositionForCurrentCharacter();
        configWindow.ApplySavedPositionForCurrentCharacter();
        PrintStatus("Reset DhogGPT window positions to 1,1 for this character.");
    }

    public bool HasDetachedConversationWindows()
        => detachedConversationWindows.Count > 0;

    public bool ShouldWindowDisplayConversation(string windowId, bool isMasterWindow, string conversationKey)
    {
        if (!detachedConversationAssignments.TryGetValue(conversationKey, out var assignedWindowId))
            return isMasterWindow;

        return assignedWindowId.Equals(windowId, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanSpawnDetachedConversationWindow(string conversationKey)
        => !detachedConversationAssignments.ContainsKey(conversationKey);

    public bool TrySpawnDetachedConversationWindow(string conversationKey, string conversationLabel)
    {
        if (string.IsNullOrWhiteSpace(conversationKey) ||
            detachedConversationAssignments.ContainsKey(conversationKey))
        {
            return false;
        }

        var windowNumber = nextDetachedWindowNumber++;
        var windowId = $"detached:{windowNumber}";
        var window = new MainWindow(
            this,
            LanguageRegistry,
            TranslationCoordinator,
            SessionHealth,
            ChatLogService,
            isMasterWindow: false,
            conversationWindowId: windowId,
            windowBadge: windowNumber.ToString());

        detachedConversationAssignments[conversationKey] = windowId;
        detachedConversationWindows[windowId] = window;
        WindowSystem.AddWindow(window);
        window.AttachDetachedConversation(conversationKey, conversationLabel);
        return true;
    }

    public void CloseDetachedConversationWindow(string windowId)
    {
        if (!detachedConversationWindows.Remove(windowId, out var window))
            return;

        var returnedConversationKeys = detachedConversationAssignments
            .Where(pair => pair.Value.Equals(windowId, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList();
        foreach (var conversationKey in returnedConversationKeys)
            detachedConversationAssignments.Remove(conversationKey);

        WindowSystem.RemoveWindow(window);
        window.Dispose();

        foreach (var conversationKey in returnedConversationKeys)
            mainWindow.ReturnDetachedConversationToPrimaryLists(conversationKey);
    }

    private void SetupDtrBar()
    {
        try
        {
            dtrEntry = DtrBar.Get(DisplayName);
            dtrEntry.OnClick = _ => OpenMainUi();
        }
        catch (Exception ex)
        {
            dtrEntry = null;
            Log.Error(ex, "[DhogGPT] Failed to setup DTR bar.");
        }
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null)
        {
            SetupDtrBar();
            if (dtrEntry == null)
                return;
        }

        try
        {
            dtrEntry.Shown = Configuration.DtrBarEnabled;
            if (!Configuration.DtrBarEnabled)
                return;

            var glyph = Configuration.PluginEnabled ? Configuration.DtrIconEnabled : Configuration.DtrIconDisabled;
            var state = Configuration.PluginEnabled ? "On" : "Off";

            dtrEntry.Text = Configuration.DtrBarMode switch
            {
                1 => new SeString(new TextPayload($"{glyph} DGPT")),
                2 => new SeString(new TextPayload(glyph)),
                _ => new SeString(new TextPayload($"DGPT: {state}")),
            };
            dtrEntry.Tooltip = new SeString(new TextPayload($"{DisplayName} {state}. Click to open the main window."));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DhogGPT] Failed to update DTR bar.");
        }
    }

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default)
            return;

        if (args.Target is not MenuTargetDefault target)
            return;

        var targetName = target.TargetName?.Trim() ?? string.Empty;
        var worldName = target.TargetHomeWorld.Value.Name.ToString();
        if (!ChatChannelMapper.TryNormalizeDirectMessageIdentity(
                ChatChannelMapper.BuildDirectMessageIdentity(targetName, worldName),
                out var normalizedIdentity,
                out _))
        {
            return;
        }

        args.AddMenuItem(new MenuItem
        {
            Name = new SeString(new TextPayload("DhogGPT: Open DM")),
            PrefixChar = 'D',
            OnClicked = _args =>
            {
                _ = Framework.RunOnFrameworkThread(() =>
                {
                    mainWindow.OpenDirectMessageConversation(normalizedIdentity);
                });
            },
        });
    }

    private void InitializeConversationVisibilityDefaults()
    {
        var changed = false;

        if (!Configuration.HasInitializedEchoChannelVisibility)
        {
            changed |= EnsureHiddenConversation("channel:ECHO");
            Configuration.HasInitializedEchoChannelVisibility = true;
        }

        if (!Configuration.HasInitializedSupplementalChannelVisibility)
        {
            changed |= EnsureHiddenConversation("channel:PROGRESS");
            changed |= EnsureHiddenConversation("channel:COMBAT");
            Configuration.HasInitializedSupplementalChannelVisibility = true;
        }

        if (changed)
            Configuration.Save();
    }

    private bool EnsureHiddenConversation(string conversationKey)
    {
        if (Configuration.HiddenGeneralConversationKeys.Any(hidden =>
                string.Equals(hidden, conversationKey, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Configuration.HiddenGeneralConversationKeys.Add(conversationKey);
        return true;
    }

    private CharacterWindowState GetOrCreateCharacterWindowState(string key)
    {
        if (Configuration.CharacterWindowStates.TryGetValue(key, out var existingState))
            return existingState;

        var state = new CharacterWindowState();
        Configuration.CharacterWindowStates[key] = state;
        return state;
    }

    private void OnLogin()
    {
        restoreLoginWindowStateWhenCharacterReady = false;
        pendingLoginWindowRestore = true;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        HandleUltraCompactFocusHotkeys();

        if (restoreLoginWindowStateWhenCharacterReady &&
            PlayerState.ContentId != 0 &&
            ObjectTable.LocalPlayer != null)
        {
            restoreLoginWindowStateWhenCharacterReady = false;
            pendingLoginWindowRestore = true;
        }

        if (!pendingLoginWindowRestore || PlayerState.ContentId == 0 || ObjectTable.LocalPlayer == null)
            return;

        pendingLoginWindowRestore = false;
        mainWindow.ApplySavedPositionForCurrentCharacter();
        configWindow.ApplySavedPositionForCurrentCharacter();
        if (Configuration.OpenMainWindowOnCharacterLogin || IsUltraCompactModeConfigured())
            mainWindow.IsOpen = true;
    }

    private void HandleUltraCompactFocusHotkeys()
    {
        var oemSlashDown = KeyState[VirtualKey.OEM_2];
        var divideSlashDown = KeyState[VirtualKey.DIVIDE];
        var slashDown = oemSlashDown || divideSlashDown;
        var enterDown = KeyState[VirtualKey.RETURN];
        var slashPressed = slashDown && !wasUltraCompactSlashDown;
        var enterPressed = enterDown && !wasUltraCompactEnterDown;

        wasUltraCompactSlashDown = slashDown;
        wasUltraCompactEnterDown = enterDown;

        if (!slashPressed && !enterPressed)
            return;

        var hotkeyName = slashPressed
            ? divideSlashDown && !oemSlashDown ? "NumpadSlash" : "Slash"
            : "Enter";

        if (!ShouldCaptureUltraCompactFocusHotkeys())
        {
            LogUltraCompactHotkeyAttempt(hotkeyName, captured: false);
            return;
        }

        if (slashPressed && Configuration.FocusUltraCompactOnSlash)
        {
            KeyState[VirtualKey.OEM_2] = false;
            KeyState[VirtualKey.DIVIDE] = false;
            mainWindow.OpenComposerFromHotkey(seedSlash: true);
            LogUltraCompactHotkeyAttempt(hotkeyName, captured: true);
            return;
        }

        if (enterPressed && Configuration.FocusUltraCompactOnEnter)
        {
            KeyState[VirtualKey.RETURN] = false;
            mainWindow.OpenComposerFromHotkey(seedSlash: false);
            LogUltraCompactHotkeyAttempt(hotkeyName, captured: true);
            return;
        }

        LogUltraCompactHotkeyAttempt(hotkeyName, captured: false);
    }

    private bool ShouldCaptureUltraCompactFocusHotkeys()
    {
        if (!IsUltraCompactModeConfigured() ||
            !mainWindow.IsOpen ||
            HasDetachedConversationWindows() ||
            configWindow.IsFocused ||
            firstUseGuideWindow.IsFocused ||
            PlayerState.ContentId == 0 ||
            ObjectTable.LocalPlayer == null ||
            !mainWindow.ShouldAllowUltraCompactFocusHotkeyCapture())
        {
            return false;
        }

        if (KeyState[VirtualKey.SHIFT] || KeyState[VirtualKey.CONTROL] || KeyState[VirtualKey.MENU])
            return false;

        return true;
    }

    private void LogUltraCompactHotkeyAttempt(string hotkeyName, bool captured)
    {
        var hasAnyWindowSystemFocus = Dalamud.Interface.Windowing.WindowSystem.HasAnyWindowSystemFocus;
        var focusedNamespace = Dalamud.Interface.Windowing.WindowSystem.FocusedWindowSystemNamespace ?? "<null>";
        var contentReady = PlayerState.ContentId != 0;
        var localPlayerReady = ObjectTable.LocalPlayer != null;
        const string capturePolicy = "IgnoreForeignWindowSystemFocus";

        Log.Information(
            $"[DhogGPT] Ultra compact hotkey {(captured ? "accepted" : "blocked")}: " +
            $"key={hotkeyName}, " +
            $"slashEnabled={Configuration.FocusUltraCompactOnSlash}, " +
            $"enterEnabled={Configuration.FocusUltraCompactOnEnter}, " +
            $"suppressVanilla={Configuration.SuppressVanillaChatWindow}, " +
            $"simpleMode={Configuration.UseSimpleChatMode}, " +
            $"compactMode={Configuration.CompactSimpleChatMode}, " +
            $"mainOpen={mainWindow.IsOpen}, " +
            $"detachedWindowsOpen={detachedConversationWindows.Count}, " +
            $"configFocused={configWindow.IsFocused}, " +
            $"guideFocused={firstUseGuideWindow.IsFocused}, " +
            $"contentReady={contentReady}, " +
            $"localPlayerReady={localPlayerReady}, " +
            $"shift={KeyState[VirtualKey.SHIFT]}, " +
            $"ctrl={KeyState[VirtualKey.CONTROL]}, " +
            $"alt={KeyState[VirtualKey.MENU]}, " +
            $"hasAnyWindowSystemFocus={hasAnyWindowSystemFocus}, " +
            $"focusedNamespace={focusedNamespace}, " +
            $"capturePolicy={capturePolicy}, " +
            $"mainWindowState={mainWindow.DescribeUltraCompactFocusHotkeyState()}");
    }
}
