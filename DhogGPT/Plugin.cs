using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
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
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/dhoggpt";
    private const string AliasCommandName = "/dgpt";
    public const string DisplayName = "DhogGPT";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("DhogGPT");
    public LanguageRegistryService LanguageRegistry { get; }
    public SessionHealthService SessionHealth { get; }
    public TranslationCacheService TranslationCache { get; }
    public LibreTranslateProvider TranslationProvider { get; }
    public TranslationCoordinator TranslationCoordinator { get; }
    public ChatTranslationService ChatTranslationService { get; }
    public ChatLogService ChatLogService { get; }

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly FirstUseGuideWindow firstUseGuideWindow;
    private IDtrBarEntry? dtrEntry;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        LanguageRegistry = new LanguageRegistryService();
        SessionHealth = new SessionHealthService();
        TranslationCache = new TranslationCacheService();
        TranslationProvider = new LibreTranslateProvider(Configuration);
        TranslationCoordinator = new TranslationCoordinator(Configuration, TranslationCache, TranslationProvider, SessionHealth);
        ChatLogService = new ChatLogService(TranslationCoordinator);
        ChatTranslationService = new ChatTranslationService(Configuration, TranslationCoordinator);

        mainWindow = new MainWindow(this, LanguageRegistry, TranslationCoordinator, SessionHealth, ChatLogService);
        configWindow = new ConfigWindow(this, LanguageRegistry);
        firstUseGuideWindow = new FirstUseGuideWindow(this);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(firstUseGuideWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open DhogGPT. Use /dhoggpt config to open settings.",
        });

        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /dhoggpt.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        SetupDtrBar();
        UpdateDtrBar();

        Log.Information("[DhogGPT] Plugin loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        WindowSystem.RemoveAllWindows();

        dtrEntry?.Remove();
        firstUseGuideWindow.Dispose();
        mainWindow.Dispose();
        configWindow.Dispose();
        ChatLogService.Dispose();
        ChatTranslationService.Dispose();
        TranslationCoordinator.Dispose();
        TranslationProvider.Dispose();

        Log.Information("[DhogGPT] Plugin unloaded.");
    }

    public void ToggleMainUi() => mainWindow.Toggle();

    public void ToggleConfigUi() => configWindow.Toggle();

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

    private void SetupDtrBar()
    {
        dtrEntry = DtrBar.Get(DisplayName);
        dtrEntry.OnClick = _ => SetPluginEnabled(!Configuration.PluginEnabled, printStatus: true);
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null)
            return;

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
        dtrEntry.Tooltip = new SeString(new TextPayload($"{DisplayName} {state}. Click to toggle translation on or off."));
    }
}
