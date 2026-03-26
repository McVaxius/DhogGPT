using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Text;
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
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/dhoggpt";
    private const string AliasCommandName = "/dgpt";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("DhogGPT");
    public LanguageRegistryService LanguageRegistry { get; }
    public SessionHealthService SessionHealth { get; }
    public TranslationCacheService TranslationCache { get; }
    public LibreTranslateProvider TranslationProvider { get; }
    public TranslationCoordinator TranslationCoordinator { get; }
    public ChatTranslationService ChatTranslationService { get; }

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        LanguageRegistry = new LanguageRegistryService();
        SessionHealth = new SessionHealthService();
        TranslationCache = new TranslationCacheService();
        TranslationProvider = new LibreTranslateProvider(Configuration);
        TranslationCoordinator = new TranslationCoordinator(Configuration, TranslationCache, TranslationProvider, SessionHealth);
        ChatTranslationService = new ChatTranslationService(Configuration, TranslationCoordinator);

        mainWindow = new MainWindow(this, LanguageRegistry, TranslationCoordinator, SessionHealth);
        configWindow = new ConfigWindow(this, LanguageRegistry);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

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

        mainWindow.Dispose();
        configWindow.Dispose();
        ChatTranslationService.Dispose();
        TranslationCoordinator.Dispose();
        TranslationProvider.Dispose();

        Log.Information("[DhogGPT] Plugin unloaded.");
    }

    public void ToggleMainUi() => mainWindow.Toggle();

    public void ToggleConfigUi() => configWindow.Toggle();

    public void PrintStatus(string message)
    {
        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = $"[DhogGPT] {message}",
        });
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
            Configuration.PluginEnabled = true;
            Configuration.Save();
            PrintStatus("Plugin enabled.");
            return;
        }

        if (trimmed.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            Configuration.PluginEnabled = false;
            Configuration.Save();
            PrintStatus("Plugin disabled.");
            return;
        }

        ToggleMainUi();
    }
}
