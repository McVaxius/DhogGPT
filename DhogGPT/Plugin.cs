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
    public const string DisplayName = "DhogGPT";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";
    public const string DiscordUrl = "https://discord.gg/VsXqydsvpu";
    public const string DiscordFeedbackNote = "Scroll down to \"The Dumpster Fire\" channel to discuss issues / suggestions for specific plugins.";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("DhogGPT");
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
    private IDtrBarEntry? dtrEntry;
    private bool pendingLoginWindowRestore;
    private bool restoreLoginWindowStateWhenCharacterReady = true;
    private bool wasUltraCompactSlashDown;
    private bool wasUltraCompactEnterDown;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        InitializeConversationVisibilityDefaults();

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
        VanillaChatWindowService = new VanillaChatWindowService(() =>
            Configuration.SuppressVanillaChatWindow &&
            Configuration.UseSimpleChatMode &&
            Configuration.CompactSimpleChatMode &&
            mainWindow.IsOpen);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(firstUseGuideWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open DhogGPT. Use /dhoggpt config for settings, /dhoggpt ultra for ultra compact mode, or /dhoggpt ws to reset window positions.",
        });

        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /dhoggpt.",
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
        CommandManager.RemoveHandler(CommandName);

        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLogin;
        WindowSystem.RemoveAllWindows();

        dtrEntry?.Remove();
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        firstUseGuideWindow.Dispose();
        mainWindow.Dispose();
        configWindow.Dispose();
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
        if (!mainWindow.IsOpen)
            mainWindow.ApplySavedPositionForCurrentCharacter();

        mainWindow.Toggle();
    }

    public void OpenMainUi()
    {
        if (!mainWindow.IsOpen)
            mainWindow.ApplySavedPositionForCurrentCharacter();

        mainWindow.IsOpen = true;
        mainWindow.RequestSimpleComposerFocus();
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
           Configuration.CompactSimpleChatMode &&
           Configuration.SuppressVanillaChatWindow;

    public void SetUltraCompactMode(bool enabled, bool printStatus = false)
    {
        if (enabled)
        {
            Configuration.UseSimpleChatMode = true;
            Configuration.CompactSimpleChatMode = true;
            Configuration.SuppressVanillaChatWindow = true;
        }
        else
        {
            Configuration.SuppressVanillaChatWindow = false;
        }

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

        mainWindow.ApplySavedPositionForCurrentCharacter();
        configWindow.ApplySavedPositionForCurrentCharacter();
        PrintStatus("Reset DhogGPT window positions to 1,1 for this character.");
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
        var slashDown = KeyState[VirtualKey.OEM_2] || KeyState[VirtualKey.DIVIDE];
        var enterDown = KeyState[VirtualKey.RETURN];
        var slashPressed = slashDown && !wasUltraCompactSlashDown;
        var enterPressed = enterDown && !wasUltraCompactEnterDown;

        wasUltraCompactSlashDown = slashDown;
        wasUltraCompactEnterDown = enterDown;

        if (!ShouldCaptureUltraCompactFocusHotkeys())
            return;

        if (slashPressed && Configuration.FocusUltraCompactOnSlash)
        {
            KeyState[VirtualKey.OEM_2] = false;
            KeyState[VirtualKey.DIVIDE] = false;
            mainWindow.OpenComposerFromHotkey(seedSlash: true);
            return;
        }

        if (enterPressed && Configuration.FocusUltraCompactOnEnter)
        {
            KeyState[VirtualKey.RETURN] = false;
            mainWindow.OpenComposerFromHotkey(seedSlash: false);
        }
    }

    private bool ShouldCaptureUltraCompactFocusHotkeys()
    {
        if (!Configuration.SuppressVanillaChatWindow ||
            !Configuration.UseSimpleChatMode ||
            !Configuration.CompactSimpleChatMode ||
            !mainWindow.IsOpen ||
            mainWindow.IsFocused ||
            configWindow.IsFocused ||
            firstUseGuideWindow.IsFocused ||
            PlayerState.ContentId == 0 ||
            ObjectTable.LocalPlayer == null)
        {
            return false;
        }

        if (KeyState[VirtualKey.SHIFT] || KeyState[VirtualKey.CONTROL] || KeyState[VirtualKey.MENU])
            return false;

        return !Dalamud.Interface.Windowing.WindowSystem.HasAnyWindowSystemFocus ||
               string.Equals(Dalamud.Interface.Windowing.WindowSystem.FocusedWindowSystemNamespace, this.WindowSystem.Namespace, StringComparison.Ordinal);
    }
}
