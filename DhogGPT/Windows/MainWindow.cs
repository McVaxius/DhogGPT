using AethertekUI;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using DhogGPT.Models;
using DhogGPT.Services;
using DhogGPT.Services.Chat;
using DhogGPT.Services.Diagnostics;
using DhogGPT.Services.Translation;

namespace DhogGPT.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion windowMotion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private const int AutoScrollSettleFrames = 2;
    private const float WindowRepairTolerance = 4f;
    private const string CombinedConversationPrefix = "combo:";
    private const char CombinedConversationSeparator = '|';
    private const string NewDirectMessagePopupId = "New DM###DhogGPTNewDmPopup";
    private const string RecentDirectMessagesPopupId = "Recent DMs###DhogGPTRecentDmPopup";
    private const string HiddenChannelsPopupId = "Hidden Channels###DhogGPTHiddenChannelsPopup";
    private const string MainWindowTitle = "###DhogGPTMain";
    private static readonly string CurrentVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    private static readonly string VersionedTitle = $"DhogGPT v{CurrentVersion}";

    private readonly Plugin plugin;
    private readonly LanguageRegistryService languageRegistry;
    private readonly TranslationCoordinator translationCoordinator;
    private readonly SessionHealthService sessionHealth;
    private readonly ChatLogService chatLogService;
    private readonly bool isMasterWindow;
    private readonly string conversationWindowId;
    private readonly string windowBadge;
    private readonly Dictionary<string, DateTimeOffset> closedConversationCutoffs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> pendingDirectMessageTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConversationScrollState> conversationScrollStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> pendingConversationBottomScrolls = new(StringComparer.OrdinalIgnoreCase);
    private readonly TitleBarButton lockTitleBarButton;

    private bool previewBusy;
    private string previewStatus = string.Empty;
    private string previewText = string.Empty;
    private string previewMetadata = string.Empty;
    private string simpleChatStatus = string.Empty;
    private string activeConversationKey = string.Empty;
    private string activeConversationLabel = string.Empty;
    private string outgoingDraft = string.Empty;
    private string outgoingDraftOriginalSeStringBase64 = string.Empty;
    private string outgoingDraftPayloadSourceText = string.Empty;
    private string restoredDirectMessageLogIdentity = string.Empty;
    private string pendingDirectMessageTarget = string.Empty;
    private string recentDirectMessageSearch = string.Empty;
    private string directMessagePopupError = string.Empty;
    private bool requestSimpleComposerFocus;
    private bool forceActiveConversationSelection;
    private bool requestOpenDirectMessagePopup;
    private bool requestOpenHiddenChannelsPopup;
    private bool requestOpenRecentDirectMessagesPopup;
    private bool requestDirectMessageTargetFocus;
    private string lastRenderedConversationBodyKey = string.Empty;
    private bool pendingSavedPositionApply;
    private bool suppressSimpleComposerAutoFocusThisFrame;
    private bool simpleComposerEditSessionActive;
    private bool requestWindowFocus;
    private bool hoveredConversationItemThisFrame;
    private bool hoveredConversationItemLastFrame;
    private bool simpleComposerFocusedLastFrame;
    private bool recentDirectMessageSearchFocusedLastFrame;
    private bool newDirectMessageTargetFocusedLastFrame;
    private bool anyPopupOpenLastFrame;
    private bool windowHoveredLastFrame;
    private bool windowFocusedLastFrame;
    private DateTimeOffset nextWindowPositionSaveUtc = DateTimeOffset.MinValue;
    private Vector2? lastSavedWindowPosition;
    private Vector2 lastObservedWindowSize;
    private Vector2? pendingViewportPlacementPosition;
    private Vector2? pendingViewportPlacementSize;
    private TrackedInputRect simpleComposerInputRect;
    private TrackedInputRect recentDirectMessageSearchInputRect;
    private TrackedInputRect newDirectMessageTargetInputRect;
    private string pendingViewportPlacementReason = string.Empty;
    private bool pendingRandomViewportPlacement;
    private bool pendingSizeConditionReset;
    private bool pendingSizeRepair;

    public MainWindow(
        Plugin plugin,
        LanguageRegistryService languageRegistry,
        TranslationCoordinator translationCoordinator,
        SessionHealthService sessionHealth,
        ChatLogService chatLogService,
        bool isMasterWindow = true,
        string conversationWindowId = "master",
        string? windowBadge = null)
        : base(isMasterWindow ? MainWindowTitle : $"###DhogGPTWindow:{conversationWindowId}")
    {
        this.plugin = plugin;
        this.languageRegistry = languageRegistry;
        this.translationCoordinator = translationCoordinator;
        this.sessionHealth = sessionHealth;
        this.chatLogService = chatLogService;
        this.isMasterWindow = isMasterWindow;
        this.conversationWindowId = conversationWindowId;
        this.windowBadge = windowBadge ?? (isMasterWindow ? "M" : "?");
        outgoingDraft = plugin.Configuration.OutgoingDraft;
        this.translationCoordinator.TranslationCompleted += OnTranslationCompleted;
        this.plugin.ChatTranslationService.IncomingDirectMessageObserved += OnIncomingDirectMessageObserved;
        lockTitleBarButton = new TitleBarButton
        {
            Click = OnLockTitleBarButtonClick,
            Icon = plugin.Configuration.LockMainWindowPosition ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            IconOffset = new Vector2(2f, 1f),
            ShowTooltip = () => UiGui.SetTooltip(plugin.Configuration.LockMainWindowPosition
                ? "Unlock main window position"
                : "Lock main window position"),
        };
        TitleBarButtons.Add(lockTitleBarButton);
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleConfigUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Settings")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Book, Priority = -20, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.OpenFirstUseGuide(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Guide")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.PowerOff, Priority = -30, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.SetPluginEnabled(!plugin.Configuration.PluginEnabled); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Enabled") + "\n" + UiText.T(plugin.Configuration.PluginEnabled ? "On" : "Off")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Compress, Priority = -40, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) TurnOnUltraCompactFromUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Turn on ultra compact") + "\n"
                + UiText.T("Switch from regular mode to ultra compact mode.") + "\n"
                + UiText.T(plugin.IsUltraCompactModeConfigured() ? "On" : "Off")),
        });

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720f, 520f),
            MaximumSize = new Vector2(1400f, 1000f),
        };
        Size = new Vector2(960f, 700f);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;
    }

    public void Dispose()
    {
        if (hoveredConversationItemLastFrame)
            Plugin.GameGui.HoveredItem = 0;

        translationCoordinator.TranslationCompleted -= OnTranslationCompleted;
        plugin.ChatTranslationService.IncomingDirectMessageObserved -= OnIncomingDirectMessageObserved;
    }

    public override void OnClose()
    {
        if (!isMasterWindow)
        {
            plugin.CloseDetachedConversationWindow(conversationWindowId);
            return;
        }

        if (!plugin.HasDetachedConversationWindows())
            return;

        IsOpen = true;
    }

    public override void PreDraw()
    {
        if (plugin.Configuration.LockMainWindowPosition)
            Flags |= ImGuiWindowFlags.NoMove;
        else
            Flags &= ~ImGuiWindowFlags.NoMove;

        lockTitleBarButton.Icon = plugin.Configuration.LockMainWindowPosition ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;

        SizeConstraints = IsUltraCompactMode()
            ? new WindowSizeConstraints
            {
                MinimumSize = new Vector2(460f, 300f),
                MaximumSize = new Vector2(1400f, 1000f),
            }
            : new WindowSizeConstraints
            {
                MinimumSize = new Vector2(720f, 520f),
                MaximumSize = new Vector2(1400f, 1000f),
            };

        ApplyPendingViewportPlacement();

        if (requestWindowFocus)
        {
            ImGui.SetNextWindowFocus();
            requestWindowFocus = false;
        }

        UiGui.ReserveTitleSpace(this, VersionedTitle, IsUltraCompactMode() ? 460 : 720);
        windowMotion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        windowMotion.Restore(this);
        plugin.ApplyWindowOpacity(windowOpacity, WindowName);
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        UiGui.TitleWithButtons(string.Empty, VersionedTitle, this);
        using var typography = new DhogGptPresentation.TextScale(IsUltraCompactMode() ? 2f : 1.25f);
        ResetTrackedInputRects();
        hoveredConversationItemThisFrame = false;
        suppressSimpleComposerAutoFocusThisFrame = false;
        anyPopupOpenLastFrame = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup);

        if (IsUltraCompactMode())
        {
            var ultraTheme=DhogGptPresentation.UltraTheme;
            ultraTheme.Density=DhogGptPresentation.Compact?MaterialDensity.Compact:MaterialDensity.Standard;
            using var palette=MaterialTheme.Push(ultraTheme,MaterialTheme.Metrics.Scale,MaterialStyleMode.ColorsOnly);
            var window=ImGuiP.GetCurrentWindow();
            ImGui.GetWindowDrawList().AddRectFilled(window.InnerRect.Min,window.InnerRect.Max,
                ImGui.GetColorU32(ultraTheme.Colors.Background));
            DrawSimpleChatMode();
            HandleSimpleComposerAutoFocusFromClick();
            UpdateHoveredConversationItemState();
            UpdateWindowOpacityState();
            TrackWindowPosition();
            return;
        }

        DrawHeader();
        ImGui.Separator();

        var uiRoot = ImGui.GetID("");
        if (ImGui.BeginChild("##DhogGptRegularBody",Vector2.Zero,false,ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGuiP.PushOverrideID(uiRoot);
            try { DrawStatusPanel(); ImGui.SetCursorPosY(ImGui.GetCursorPosY()+(DhogGptPresentation.Compact?6:2)*MaterialTheme.Metrics.Scale); DrawComposer(); }
            finally { ImGui.PopID(); }
        }
        ImGui.EndChild();
        UpdateHoveredConversationItemState();
        UpdateWindowOpacityState();
        TrackWindowPosition();
    }


    private void TurnOnUltraCompactFromUi()
    {
        if (!plugin.IsUltraCompactModeConfigured()) plugin.SetUltraCompactMode(true);
    }

    private void DrawHeader()
    {
        var configuration=plugin.Configuration;
        var ultra=IsUltraCompactMode();
        using var headerSpacing=new MaterialStyleScope();
        headerSpacing.Style(ImGuiStyleVar.ItemSpacing,new Vector2(ImGui.GetStyle().ItemSpacing.X,(DhogGptPresentation.Compact?7:5)*MaterialTheme.Metrics.Scale));
        DhogGptPresentation.Brand(ultra);
        DhogGptPresentation.SameLineIfFits(MaterialText.Measure(windowBadge).X);
        MaterialText.TextDisabled(windowBadge);
        var scale=MaterialTheme.Metrics.Scale;
        var metrics=MaterialControlMetrics.Measure(MaterialTheme.Metrics,ImGui.GetFontSize());
        var languageName=UiText.Languages.Single(value=>value.Code==UiText.Current.Language).Name;
        var languageWidth=Math.Max(130*scale,MathF.Ceiling(MaterialText.Measure(languageName).X+metrics.Height+metrics.Gap*3+Math.Min(metrics.IconSize,metrics.Height)));
        var gap = ImGui.GetStyle().ItemSpacing.X;
        if (configuration.UiCompactVisibleOnMainWindow)
        {
            DhogGptPresentation.SameLineIfFits(ImGui.GetFrameHeight() + MaterialText.Measure("C").X + gap);
            plugin.DrawUiCompact(header: true);
        }
        if (configuration.UiLanguageVisibleOnMainWindow)
        {
            DhogGptPresentation.SameLineIfFits(languageWidth);
            plugin.DrawUiLanguage();
        }
        DhogGptPresentation.SameLineIfFits(ImGui.GetFrameHeight() + MaterialText.Measure(UiText.T("Transparency")).X + gap);
        plugin.DrawTransparency();
        if(ultra)return;
        if(UiGui.Button("Guide",icon:MaterialIcon.Book))plugin.OpenFirstUseGuide();
        Next("Settings");if(UiGui.Button("Settings",icon:MaterialIcon.Settings))plugin.ToggleConfigUi();
        Next("Status to chat");if(UiGui.Button("Status to chat",icon:MaterialIcon.Chat))plugin.PrintStatus("DhogGPT is loaded and ready.");
        // Ko-fi and Discord originally lived within this native header table's ID scope.
        var supportRoot=ImGui.GetID("DhogGPTHeaderTop");
        Next("Ko-fi");ImGuiP.PushOverrideID(supportRoot);
        if(UiGui.Button("Ko-fi",icon:MaterialIcon.Heart))Process.Start(new ProcessStartInfo { FileName=Plugin.SupportUrl,UseShellExecute=true });
        ImGui.PopID();
        Next("Discord");ImGuiP.PushOverrideID(supportRoot);
        if(UiGui.Button("Discord",icon:MaterialIcon.Group))Process.Start(new ProcessStartInfo { FileName=Plugin.DiscordUrl,UseShellExecute=true });
        ImGui.PopID();
        Next("Enabled");
        var enabled=configuration.PluginEnabled;
        if(UiGui.Checkbox("Enabled",ref enabled))plugin.SetPluginEnabled(enabled);
        Next("DTR Bar");
        var dtrEnabled=configuration.DtrBarEnabled;
        if(UiGui.Checkbox("DTR Bar",ref dtrEnabled)) { configuration.DtrBarEnabled=dtrEnabled;configuration.Save();plugin.UpdateDtrBar(); }
        Next("Turn on ultra compact");
        if(UiGui.Button("Turn on ultra compact"))TurnOnUltraCompactFromUi();
        if(ImGui.IsItemHovered())UiGui.SetTooltip("Switch from regular mode to ultra compact mode.");
        var krangle=configuration.KrangleChatNames?"Krangle Names: On":"Krangle Names: Off";
        Next(krangle);
        if(UiGui.Button(krangle)) { configuration.KrangleChatNames=!configuration.KrangleChatNames;configuration.Save(); }
        void Next(string label)=>DhogGptPresentation.SameLineIfFits(MaterialText.Measure(UiText.T(label)).X+ImGui.GetStyle().FramePadding.X*2+ImGui.GetFrameHeight());
    }

    private void DrawSimpleChatMode()
    {
        HandlePendingPopups();
        var scale=MaterialTheme.Metrics.Scale;
        var window=ImGuiP.GetCurrentWindow();
        var spacing=ImGui.GetStyle().ItemSpacing.Y;
        var footerHeight=(DhogGptPresentation.Compact?50:58)*scale;
        var footerY=window.InnerRect.Max.Y-ImGui.GetStyle().WindowPadding.Y-footerHeight;
        var footerReserve=footerHeight+spacing*2+scale;
        ImGui.PushClipRect(window.ClipRect.Min,new Vector2(window.ClipRect.Max.X,Math.Max(window.ClipRect.Min.Y,footerY-spacing*2-scale)),true);
        DrawUltraAppearanceHeader();
        DrawSimpleChatStatusBanner();
        DrawSimpleLanguageBar();

        var composerHeight = (DhogGptPresentation.Compact?50:58)*scale+ImGui.GetStyle().ItemSpacing.Y*2;
        var conversationHeaderHeight = (DhogGptPresentation.UltraTabHeight+16)*scale+ImGui.GetStyle().ItemSpacing.Y;
        var chatBodyHeight = Math.Max(1f, ImGui.GetContentRegionAvail().Y - composerHeight - conversationHeaderHeight);
        DrawTabbedConversationArea(chatBodyHeight);
        ImGui.PopClipRect();
        var contentMax=window.DC.CursorMaxPos;
        ImGui.SetCursorScreenPos(new Vector2(window.Pos.X+ImGui.GetStyle().WindowPadding.X-window.Scroll.X,footerY-spacing-scale));
        ImGui.Separator();
        DrawSimpleComposer();
        // Reserve the visible footer in native scroll extent without feeding scroll offset back into layout.
        window.DC.CursorMaxPos=new Vector2(window.DC.CursorMaxPos.X,
            Math.Max(window.InnerRect.Max.Y-ImGui.GetStyle().WindowPadding.Y-window.Scroll.Y,contentMax.Y+footerReserve));
        DrawDirectMessageCreationPopup();
    }

    private void DrawUltraAppearanceHeader()
    {
        using var preferenceSize = new DhogGptPresentation.TextScale(.625f);
        using (var brandSize = new DhogGptPresentation.TextScale(1.8f))
            DhogGptPresentation.Text("DhogGPT", UiFontRole.Body, MaterialTheme.Current.Colors.OnSurface, 38);
        var scale = MaterialTheme.Metrics.Scale;
        if (plugin.Configuration.UiCompactVisibleOnMainWindow)
        {
            DhogGptPresentation.SameLineIfFits(ImGui.GetFrameHeight() + MaterialText.Measure("C").X + ImGui.GetStyle().ItemInnerSpacing.X);
            plugin.DrawUiCompact(header: true);
        }
        if (plugin.Configuration.UiLanguageVisibleOnMainWindow)
        {
            DhogGptPresentation.SameLineIfFits(180 * scale);
            plugin.DrawUiLanguage();
        }
        DhogGptPresentation.SameLineIfFits(ImGui.GetFrameHeight() + MaterialText.Measure(UiText.T("Transparency")).X + ImGui.GetStyle().ItemInnerSpacing.X);
        plugin.DrawTransparency();
    }

    private void DrawSimpleLanguageBar()
    {
        var configuration = plugin.Configuration;
        var changed = false;

        var useInlineCompactBar = IsUltraCompactMode();
        if (useInlineCompactBar)
        {
            EnsureUltraCompactLanguageDefaults();
            var originalSpacing = ImGui.GetStyle().ItemSpacing;
            var originalFramePadding = ImGui.GetStyle().FramePadding;
            var scale=MaterialTheme.Metrics.Scale;
            var fieldHeight=DhogGptPresentation.UltraFieldHeight*scale;
            var compactSpacing = new Vector2((DhogGptPresentation.Compact?12:18)*scale,8*scale);
            var compactFramePadding = new Vector2(16*scale,Math.Max(0,(fieldHeight-ImGui.GetFontSize())*.5f));
            var labelWidth = Math.Max(MaterialText.Measure(UiText.T("Them")).X, MaterialText.Measure(UiText.T("Me")).X);
            var actionsWidth=(52+52+98)*scale+compactSpacing.X*3;
            var comboWidth = Math.Max(90f*scale,Math.Min(296f*scale,
                (ImGui.GetContentRegionAvail().X-labelWidth*2f-compactSpacing.X*3f-actionsWidth)*.5f));
            var languageGroupWidth = labelWidth + compactSpacing.X + comboWidth;

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, compactSpacing);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, compactFramePadding);
            ImGui.BeginGroup();
            ImGui.AlignTextToFramePadding();
            UiGui.TextUnformatted("Me");
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Your typed language before DhogGPT translates it.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(comboWidth);
            changed |= DrawLanguageCombo(
                "##SimpleMeLanguage",
                configuration.OutgoingSourceLanguage,
                value =>
                {
                    configuration.OutgoingSourceLanguage = value;
                    configuration.IncomingSourceLanguage = "auto";
                    configuration.IncomingTargetLanguage = value;
                },
                includeAuto: false);
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Set the language you are writing in.");
            ImGui.EndGroup();

            DhogGptPresentation.SameLineIfFits(languageGroupWidth);
            ImGui.BeginGroup();
            ImGui.AlignTextToFramePadding();
            UiGui.TextUnformatted("Them");
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("The language DhogGPT should translate your outgoing text into.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(comboWidth);
            changed |= DrawLanguageCombo(
                "##SimpleThemLanguage",
                configuration.OutgoingTargetLanguage,
                value => configuration.OutgoingTargetLanguage = value,
                includeAuto: false);
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Set the language your messages should be translated to.");
            ImGui.EndGroup();
            DhogGptPresentation.SameLineIfFits(52*scale);
            var highlightKrangleButton = configuration.KrangleChatNames;
            if (highlightKrangleButton)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.56f, 0.32f, 0.95f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.66f, 0.38f, 0.95f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.24f, 0.48f, 0.28f, 0.95f));
            }
            if (UiGui.Button("K##UltraCompactKrangle",new Vector2(52*scale,0)))
            {
                configuration.KrangleChatNames = !configuration.KrangleChatNames;
                changed = true;
            }
            if (highlightKrangleButton)
                ImGui.PopStyleColor(3);
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip(configuration.KrangleChatNames ? "Krangle names is on." : "Krangle names is off.");

            DhogGptPresentation.SameLineIfFits(52*scale);
            if (UiGui.Button("S##UltraCompactSettings",new Vector2(52*scale,0)))
                plugin.ToggleConfigUi();
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Open DhogGPT settings.");

            DhogGptPresentation.SameLineIfFits(98*scale);
            if (UiGui.Button("Ko-fi##UltraCompactSupport",new Vector2(98*scale,0)))
                Process.Start(new ProcessStartInfo { FileName = Plugin.SupportUrl, UseShellExecute = true });
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Open the DhogGPT support page.");
            ImGui.PopStyleVar(2);

            if (changed)
                configuration.Save();

            return;
        }

        if (ImGui.BeginTable("DhogGPTSimpleLanguageTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            UiGui.TextUnformatted("Outgoing");
            changed |= DrawLanguageCombo("From##SimpleOutgoing", configuration.OutgoingSourceLanguage, value => configuration.OutgoingSourceLanguage = value, includeAuto: true);
            changed |= DrawLanguageCombo("To##SimpleOutgoing", configuration.OutgoingTargetLanguage, value => configuration.OutgoingTargetLanguage = value, includeAuto: false);

            ImGui.TableSetColumnIndex(1);
            UiGui.TextUnformatted("Incoming");
            changed |= DrawLanguageCombo("From##SimpleIncoming", configuration.IncomingSourceLanguage, value => configuration.IncomingSourceLanguage = value, includeAuto: true);
            changed |= DrawLanguageCombo("To##SimpleIncoming", configuration.IncomingTargetLanguage, value => configuration.IncomingTargetLanguage = value, includeAuto: false);

            ImGui.EndTable();
        }

        if (changed)
            configuration.Save();
    }

    private void DrawTabbedConversationArea(float height)
    {
        var entries = chatLogService.GetEntriesSnapshot();
        var currentLogIdentity = chatLogService.GetCurrentLogIdentitySnapshot() ?? string.Empty;
        var recordedConversations = entries
            .GroupBy(GetConversationGroupingKey)
            .Select(group =>
            {
                var messages = group.OrderBy(entry => entry.TimestampUtc).ToList();
                var resolvedLabel = ResolveConversationLabel(messages);
                if (pendingDirectMessageTabs.TryGetValue(group.Key, out var pendingLabel) &&
                    pendingLabel.Contains('@') &&
                    !resolvedLabel.Contains('@'))
                {
                    resolvedLabel = pendingLabel;
                }

                return new ConversationTabState(
                    group.Key,
                    resolvedLabel,
                    messages,
                    messages.Count > 0 ? messages[^1].TimestampUtc : DateTimeOffset.MinValue);
            })
            .OrderByDescending(state => state.LastMessageUtc)
            .ToList();

        foreach (var recordedDirectMessageKey in recordedConversations
                     .Where(conversation => IsDirectMessageConversation(conversation.Key))
                     .Select(conversation => conversation.Key)
                     .ToList())
        {
            pendingDirectMessageTabs.Remove(recordedDirectMessageKey);
        }

        var conversations = new List<ConversationTabState>();
        var remainingConversations = recordedConversations.ToDictionary(state => state.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var pinnedConversation in GetPinnedGeneralConversations())
        {
            if (remainingConversations.Remove(pinnedConversation.Key, out var existingConversation))
                conversations.Add(existingConversation with { Label = pinnedConversation.Label });
            else if (TryResolveConversationTabState(pinnedConversation.Key, out var resolvedConversation))
                conversations.Add(resolvedConversation with { Label = pinnedConversation.Label });
            else
                conversations.Add(pinnedConversation);
        }
        var configuredConversation = GetPreferredConversationForInsertion();
        if (ShouldInsertConfiguredConversation(configuredConversation) &&
            !remainingConversations.ContainsKey(configuredConversation.Key) &&
            !conversations.Any(state => state.Key.Equals(configuredConversation.Key, StringComparison.OrdinalIgnoreCase)))
        {
            remainingConversations[configuredConversation.Key] = new ConversationTabState(
                configuredConversation.Key,
                configuredConversation.Label,
                new List<TranslationHistoryItem>(),
                DateTimeOffset.MinValue);
        }

        foreach (var pendingConversation in pendingDirectMessageTabs)
        {
            if (remainingConversations.ContainsKey(pendingConversation.Key))
                continue;

            remainingConversations[pendingConversation.Key] = new ConversationTabState(
                pendingConversation.Key,
                pendingConversation.Value,
                new List<TranslationHistoryItem>(),
                DateTimeOffset.MinValue);
        }

        var allDirectMessageConversations = remainingConversations.Values
            .Where(conversation => IsDirectMessageConversation(conversation.Key))
            .OrderByDescending(conversation => conversation.LastMessageUtc)
            .ThenBy(conversation => conversation.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var visibleDirectMessageConversations = allDirectMessageConversations
            .Where(conversation => ShouldWindowDisplayConversation(conversation.Key))
            .ToList();

        ApplyDefaultDirectMessageVisibility(currentLogIdentity, allDirectMessageConversations);

        conversations.AddRange(visibleDirectMessageConversations.Where(IsConversationVisible));

        if (string.IsNullOrWhiteSpace(activeConversationKey) || conversations.All(state => !state.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase)))
        {
            activeConversationKey = conversations.FirstOrDefault()?.Key ?? string.Empty;
            activeConversationLabel = conversations.FirstOrDefault()?.Label ?? string.Empty;
            forceActiveConversationSelection = true;
        }

        var selectedConversationApplied = false;
        ConversationTabState? selectedConversation = null;
        var toolbarWidth = GetConversationToolbarWidth();
        var style = ImGui.GetStyle();
        var ultra=IsUltraCompactMode();var scale=MaterialTheme.Metrics.Scale;
        var tabHeight=DhogGptPresentation.UltraTabHeight*scale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style.CellPadding.X, ultra?8*scale:0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, ultra?8*scale:1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, ultra?Math.Max(0,(tabHeight-ImGui.GetFontSize())*.5f):Math.Min(style.FramePadding.Y, 2f)));
        var pane=ImGuiP.GetCurrentWindow();
        var visibleWidth=Math.Max(1,pane.InnerRect.Max.X-style.WindowPadding.X-(ImGui.GetCursorScreenPos().X+pane.Scroll.X));
        if (!ImGui.BeginTable("DhogGPTConversationTabsLayout", 2, ImGuiTableFlags.SizingStretchProp,new Vector2(visibleWidth,0)))
        {
            ImGui.PopStyleVar(3);
            DrawHiddenChannelsPopup();
            DrawRecentDirectMessagesPopup(visibleDirectMessageConversations);
            return;
        }

        ImGui.TableSetupColumn("Tabs", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, toolbarWidth);
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        HandleConversationTabWheelNavigation(conversations);
        var tabPalette = GetConversationTabPalette();
        PushConversationTabStyleColors(
            tabPalette.Tab,
            tabPalette.TabHovered,
            tabPalette.TabActive,
            tabPalette.TabUnfocused,
            tabPalette.TabUnfocusedActive);
        if (!UiGui.BeginTabBar("DhogGPTConversationTabs", conversations.Select(conversation => IsDirectMessageConversation(conversation.Key) ? "   "+GetConversationDisplayLabel(conversation) : UiText.T(GetConversationDisplayLabel(conversation))).ToArray(), ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.Reorderable))
        {
            ImGui.PopStyleColor(5);
            ImGui.TableSetColumnIndex(1);
            DrawConversationToolbarButtons();
            ImGui.EndTable();
            ImGui.PopStyleVar(3);
            DrawHiddenChannelsPopup();
            DrawRecentDirectMessagesPopup(visibleDirectMessageConversations);
            return;
        }

        foreach (var conversation in conversations)
        {
            var displayLabel = GetConversationDisplayLabel(conversation);
            var isDirectMessage = IsDirectMessageConversation(conversation.Key);
            var isGeneralConversation = !isDirectMessage;
            var isPinnedDirectMessage = isDirectMessage && IsPinnedDirectMessageConversation(conversation.Key);
            var isRequestedConversation = conversation.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase);
            var tabFlags = isRequestedConversation && forceActiveConversationSelection
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var tabOpen = true;
            var tabLabel = isDirectMessage
                ? $"   {displayLabel}##{conversation.Key}"
                : $"{displayLabel}##{conversation.Key}";
            var tabTextColor = isRequestedConversation ? tabPalette.ActiveTabText : tabPalette.TabText;
            ImGui.PushStyleColor(ImGuiCol.Text, tabTextColor);
            var tabVisible = UiGui.BeginTabItem(tabLabel, ref tabOpen, tabFlags,translate:!isDirectMessage);
            ImGui.PopStyleColor();
            if (isDirectMessage)
                DrawDirectMessageTabPin(conversation, isPinnedDirectMessage);

            if (!tabVisible)
            {
                if (isGeneralConversation && !tabOpen)
                    CloseGeneralConversation(conversation);

                if (isDirectMessage && !tabOpen)
                    CloseConversation(conversation, isPinnedDirectMessage);

                continue;
            }

            if (isGeneralConversation && ImGui.IsItemHovered())
                UiGui.SetTooltip("Hold Ctrl and click x to hide this channel tab.");

            if (isDirectMessage)
                DrawDirectMessageTabContextMenu(conversation, isPinnedDirectMessage);
            else
                DrawGeneralConversationContextMenu(conversation);

            if (forceActiveConversationSelection && !isRequestedConversation)
            {
                ImGui.EndTabItem();

                if (isGeneralConversation && !tabOpen)
                    CloseGeneralConversation(conversation);

                if (isDirectMessage && !tabOpen)
                    CloseConversation(conversation, isPinnedDirectMessage);

                continue;
            }

            activeConversationKey = conversation.Key;
            activeConversationLabel = conversation.Label;
            selectedConversationApplied = true;
            selectedConversation = conversation;
            if (SyncOutgoingChannelToConversation(conversation))
                plugin.Configuration.Save();
            ImGui.EndTabItem();

            if (isGeneralConversation && !tabOpen)
                CloseGeneralConversation(conversation);

            if (isDirectMessage && !tabOpen)
                CloseConversation(conversation, isPinnedDirectMessage);
        }

        ImGui.EndTabBar();
        ImGui.PopStyleColor(5);
        ImGui.TableSetColumnIndex(1);
        DrawConversationToolbarButtons();
        ImGui.EndTable();
        ImGui.PopStyleVar(3);
        DrawHiddenChannelsPopup();
        DrawRecentDirectMessagesPopup(visibleDirectMessageConversations);
        if (selectedConversationApplied)
            forceActiveConversationSelection = false;

        if(ultra)
        {
            var remaining=pane.InnerRect.Max.Y-style.WindowPadding.Y-(ImGui.GetCursorScreenPos().Y+pane.Scroll.Y);
            height=Math.Max(1,remaining-(DhogGptPresentation.Compact?50:58)*scale-scale-style.ItemSpacing.Y*2);
        }
        DrawConversationMessages(selectedConversation?.Key ?? activeConversationKey, selectedConversation?.Messages ?? Array.Empty<TranslationHistoryItem>(), height);
    }

    private void DrawConversationMessages(string conversationKey, IReadOnlyList<TranslationHistoryItem> messages, float height)
    {
        conversationKey ??= string.Empty;
        var currentLastMessageTicks = messages.Count > 0 ? messages[^1].TimestampUtc.UtcTicks : 0L;
        conversationScrollStates.TryGetValue(conversationKey, out var existingScrollState);
        var conversationChanged = !string.Equals(lastRenderedConversationBodyKey, conversationKey, StringComparison.OrdinalIgnoreCase);
        var messagesChanged = existingScrollState.MessageCount != messages.Count || existingScrollState.LastMessageTicks != currentLastMessageTicks;
        if (conversationChanged || messagesChanged)
            pendingConversationBottomScrolls[conversationKey] = AutoScrollSettleFrames;

        var parent=ImGuiP.GetCurrentWindow();
        var bodyWidth=IsUltraCompactMode()?Math.Max(1,parent.InnerRect.Max.X-ImGui.GetStyle().WindowPadding.X-(ImGui.GetCursorScreenPos().X+parent.Scroll.X)):-1;
        using var bodyStyle=new MaterialStyleScope();
        if(IsUltraCompactMode())bodyStyle.Style(ImGuiStyleVar.WindowPadding,new Vector2(6,DhogGptPresentation.Compact?16:28)*MaterialTheme.Metrics.Scale);
        if (!ImGui.BeginChild(
                "DhogGPTConversationBody",
                new Vector2(bodyWidth, height),
                !IsUltraCompactMode(),
                ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            ImGui.EndChild();
            return;
        }

        pendingConversationBottomScrolls.TryGetValue(conversationKey, out var pendingBottomScrollFrames);
        var shouldAutoScrollThisFrame = pendingBottomScrollFrames > 0;

        if (messages.Count == 0)
        {
            UiGui.TextDisabled("No translated chat has been logged for this tab yet.");
            conversationScrollStates[conversationKey] = new ConversationScrollState(messages.Count, currentLastMessageTicks);
            lastRenderedConversationBodyKey = conversationKey;
            pendingConversationBottomScrolls.Remove(conversationKey);
            ImGui.EndChild();
            return;
        }

        var originalSpacing = ImGui.GetStyle().ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(originalSpacing.X, 1f));

        var orderedMessages = messages.OrderBy(entry => entry.TimestampUtc).ToList();
        for (var messageIndex = 0; messageIndex < orderedMessages.Count; messageIndex++)
        {
            var message = orderedMessages[messageIndex];
            var (headerColor, translatedColor, errorColor) = GetMessagePalette(message);
            var timestamp = message.TimestampUtc.ToLocalTime().ToString("HH:mm", UiText.Current.Culture);
            var displayName = GetDisplayName(message);
            var originalText = string.IsNullOrWhiteSpace(message.OriginalText) ? UiText.T("(empty)") : message.OriginalText;
            var canShowTranslatedLine = ShouldShowTranslatedLine(message);
            var clipboardText = canShowTranslatedLine
                ? $"{timestamp} - {displayName} - {originalText}{Environment.NewLine}{message.TranslatedText}"
                : $"{timestamp} - {displayName} - {originalText}";

            ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
            MaterialText.Text($"{timestamp} - {displayName} - ");
            ImGui.PopStyleColor();
            ImGui.SameLine(0f, 0f);

            if (!TryDrawOriginalMessagePayload(message, messageIndex))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
                MaterialText.TextWrapped(originalText);
                ImGui.PopStyleColor();
            }

            if (message.Success && canShowTranslatedLine)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, translatedColor);
                MaterialText.TextWrapped(message.TranslatedText);
                ImGui.PopStyleColor();
            }
            else if (!string.IsNullOrWhiteSpace(message.Error))
            {
                UiGui.TextColored(errorColor, UiText.F("Translation failed: {0}",message.Error));
            }

            if (ImGui.BeginPopupContextItem($"DhogGPTMessageContext##{message.TimestampUtc.UtcTicks}{message.ConversationKey}"))
            {
                if (UiGui.Selectable("Copy original"))
                    ImGui.SetClipboardText(originalText);

                if (canShowTranslatedLine && UiGui.Selectable("Copy translation"))
                    ImGui.SetClipboardText(message.TranslatedText);

                if (UiGui.Selectable("Copy both"))
                    ImGui.SetClipboardText(clipboardText);

                ImGui.EndPopup();
            }

            ImGui.Dummy(new Vector2(0f, 2f));

            if (shouldAutoScrollThisFrame && messageIndex == orderedMessages.Count - 1)
                ImGui.SetScrollHereY(1f);
        }

        ImGui.PopStyleVar();
        ImGui.Dummy(Vector2.Zero);
        var canScrollUp = ImGui.GetScrollY() > 1f;
        var canScrollDown = ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1f;
        DrawConversationScrollIndicators(canScrollUp, canScrollDown);

        if (shouldAutoScrollThisFrame)
        {
            if (pendingBottomScrollFrames <= 1)
                pendingConversationBottomScrolls.Remove(conversationKey);
            else
                pendingConversationBottomScrolls[conversationKey] = pendingBottomScrollFrames - 1;
        }

        conversationScrollStates[conversationKey] = new ConversationScrollState(messages.Count, currentLastMessageTicks);
        lastRenderedConversationBodyKey = conversationKey;
        ImGui.EndChild();
    }

    private void DrawSimpleComposer()
    {
        var configuration = plugin.Configuration;
        var changed = SyncSimpleComposerToActiveConversation();
        var ultraCompactMode = IsUltraCompactMode();
        var showOutgoingCombo = !ultraCompactMode && isMasterWindow;

        var comboWidth = 150f;
        var framePadding = ImGui.GetStyle().FramePadding;
        var composerFramePadding = ultraCompactMode
            ? new Vector2(20*MaterialTheme.Metrics.Scale,Math.Max(0,((DhogGptPresentation.Compact?50:58)*MaterialTheme.Metrics.Scale-ImGui.GetFontSize())*.5f))
            : new Vector2(framePadding.X, Math.Max(framePadding.Y, 6f));
        var sendWidth = ultraCompactMode
            ? MaterialText.Measure("Send").X + (composerFramePadding.X * 2f)
            : 70f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var entryWidth = Math.Max(120f, ImGui.GetContentRegionAvail().X - (showOutgoingCombo ? comboWidth + spacing : 0f) - sendWidth - spacing);
        var submitFromEnter = false;
        var composerFrameOpacity = simpleComposerEditSessionActive
            ? 1.0f
            : GetInactiveSimpleComposerOpacity();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, composerFramePadding);
        if (showOutgoingCombo)
        {
            ImGui.SetNextItemWidth(comboWidth);
            if (DrawOutgoingChannelCombo("##SimpleChannel"))
                changed = true;

            ImGui.SameLine();
        }

        if (requestSimpleComposerFocus)
        {
            ImGui.SetKeyboardFocusHere();
            requestSimpleComposerFocus = false;
        }

        ImGui.SetNextItemWidth(ultraCompactMode ? -1f : entryWidth);
        var styleColors = ImGui.GetStyle().Colors;
        var composerBackground=ultraCompactMode?MaterialTheme.Current.Colors.SurfaceVariant:styleColors[(int)ImGuiCol.FrameBg];
        ImGui.PushStyleColor(ImGuiCol.FrameBg, WithMinimumAlpha(composerBackground, composerFrameOpacity));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, WithMinimumAlpha(styleColors[(int)ImGuiCol.FrameBgHovered], composerFrameOpacity));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, WithMinimumAlpha(styleColors[(int)ImGuiCol.FrameBgActive], composerFrameOpacity));
        var draft = outgoingDraft;
        submitFromEnter = UiGui.InputTextWithHint(
            "##SimpleChatEntry",
            "Translate this text and press Enter to send",
            ref draft,
            2000,
            ImGuiInputTextFlags.EnterReturnsTrue);
        var composerIsFocused = ImGui.IsItemFocused();
        var composerIsActive = ImGui.IsItemActive() || composerIsFocused;
        var composerLostFocus = ImGui.IsItemDeactivated();
        simpleComposerInputRect = TrackedInputRect.CaptureCurrentItem();
        simpleComposerFocusedLastFrame = composerIsFocused;
        ImGui.PopStyleColor(3);
        simpleComposerEditSessionActive = composerIsActive;
        if (draft != outgoingDraft)
        {
            outgoingDraft = draft;
            if (isMasterWindow)
                configuration.OutgoingDraft = draft;
            ClearTransientUiStatus();
            changed = true;
        }

        if (submitFromEnter || composerLostFocus)
            simpleComposerEditSessionActive = false;

        if (!ultraCompactMode)
        {
            ImGui.SameLine();
            if (UiGui.Button("Send##SimpleSend", new Vector2(sendWidth, 0f)))
                submitFromEnter = true;
        }
        ImGui.PopStyleVar();

        if (submitFromEnter)
        {
            if (string.IsNullOrWhiteSpace(outgoingDraft))
            {
                requestSimpleComposerFocus = false;
                suppressSimpleComposerAutoFocusThisFrame = true;
                ImGui.SetWindowFocus((string?)null);
            }
            else
            {
                _ = SendSimpleChatAsync();
            }
        }

        if (changed)
        {
            if (isMasterWindow)
                configuration.Save();
        }
    }


    private void DrawStatusPanel()
    {
        var snapshot=sessionHealth.GetSnapshot();
        using var panel=new DhogGptPresentation.Panel("##DhogGptStatusPanel");
        if(!panel.Visible)return;
        var scale=MaterialTheme.Metrics.Scale;
        using var statusSpacing=new MaterialStyleScope();
        statusSpacing.Style(ImGuiStyleVar.ItemSpacing,new Vector2(12,4)*scale);
        statusSpacing.Style(ImGuiStyleVar.CellPadding,new Vector2(14,7)*scale);
        if (DhogGptPresentation.Compact)
        {
            var columns=ImGui.GetContentRegionAvail().X>=800*scale?6:2;
            statusSpacing.Style(ImGuiStyleVar.CellPadding,new Vector2(12,0)*scale);
            if(ImGui.BeginTable("##DhogGptCompactSummary",columns,ImGuiTableFlags.NoSavedSettings|ImGuiTableFlags.SizingStretchProp|ImGuiTableFlags.BordersInnerV))
            {
                if(columns==6)
                {
                    using var headingSize=new DhogGptPresentation.TextScale(1.3f);
                    using var headingFont=UiText.Font(UiFontRole.BodyStrong);
                    ImGui.TableSetupColumn("##Status",ImGuiTableColumnFlags.WidthFixed,Math.Max(70*scale,MathF.Ceiling(MaterialText.Measure(UiText.T("Status")).X)));
                }
                var weights=new[]{176f,199f,168f,289f,297f};
                for(var index=columns==6?1:0;index<columns;index++)ImGui.TableSetupColumn("##Summary"+index,ImGuiTableColumnFlags.WidthStretch,columns==6?weights[index-1]:1);
                ImGui.TableNextRow(ImGuiTableRowFlags.None,38*scale);
                ImGui.TableNextColumn();ImGui.AlignTextToFramePadding();
                using(var headingSize=new DhogGptPresentation.TextScale(1.3f))
                    DhogGptPresentation.Text("Status",UiFontRole.BodyStrong,MaterialTheme.Current.Colors.OnSurface,20);
                InlineSummary("Jobs",UiText.F("{0}",snapshot.QueueDepth),null,true);
                InlineSummary("Success",UiText.F("{0}",snapshot.SuccessCount),DhogGptPresentation.Success,true);
                InlineSummary("Fail",UiText.F("{0}",snapshot.FailureCount),DhogGptPresentation.Failure,true);
                InlineSummary("Provider",string.IsNullOrWhiteSpace(snapshot.LastProvider)?"—":snapshot.LastProvider,null,false);
                if(!string.IsNullOrWhiteSpace(snapshot.LastEndpoint) && ImGui.IsItemHovered())MaterialText.SetTooltip(snapshot.LastEndpoint);
                InlineSummary("Latency",snapshot.LastLatency>TimeSpan.Zero?UiText.F("{0:F0} ms",snapshot.LastLatency.TotalMilliseconds):"—",null,false);
                ImGui.EndTable();
            }
            if(!string.IsNullOrWhiteSpace(snapshot.LastError))UiGui.TextWrapped(UiText.F("Last error: {0}",snapshot.LastError));
            return;
        }
        DhogGptPresentation.Section("Status");
        var narrow=ImGui.GetContentRegionAvail().X<640*MaterialTheme.Metrics.Scale;
        if(ImGui.BeginTable("##DhogGptSummary",narrow?2:5,ImGuiTableFlags.NoSavedSettings|ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None,66*scale);
            Summary("Jobs",UiText.F("{0}",snapshot.QueueDepth),null,true);
            Summary("Success",UiText.F("{0}",snapshot.SuccessCount),DhogGptPresentation.Success,true);
            Summary("Fail",UiText.F("{0}",snapshot.FailureCount),DhogGptPresentation.Failure,true);
            Summary("Provider",string.IsNullOrWhiteSpace(snapshot.LastProvider)?"—":snapshot.LastProvider,null,false);
            Summary("Latency",snapshot.LastLatency>TimeSpan.Zero?UiText.F("{0:F0} ms",snapshot.LastLatency.TotalMilliseconds):"—",null,false);
            ImGui.EndTable();
        }
        if(!string.IsNullOrWhiteSpace(snapshot.LastEndpoint) && ImGui.IsItemHovered())UiGui.SetTooltip(snapshot.LastEndpoint);
        if(!string.IsNullOrWhiteSpace(snapshot.LastError))UiGui.TextWrapped(UiText.F("Last error: {0}",snapshot.LastError));
        void InlineSummary(string label,string value,Vector4? color,bool number)
        {
            ImGui.TableNextColumn();
            ImGui.BeginGroup();
            ImGui.AlignTextToFramePadding();MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant,UiText.T(label));
            using var valueSize=new DhogGptPresentation.TextScale(number?.8f:1f);
            float width;using(UiText.Font(number?UiFontRole.Title:UiFontRole.BodyStrong))width=MaterialText.Measure(value).X;
            DhogGptPresentation.SameLineIfFits(width);
            using(UiText.Font(number?UiFontRole.Title:UiFontRole.BodyStrong))MaterialText.TextColored(color??MaterialTheme.Current.Colors.OnSurface,value);
            ImGui.EndGroup();
        }
        void Summary(string label,string value,Vector4? color,bool number)
        {
            ImGui.TableNextColumn();
            var left=ImGui.GetCursorPosX();var width=ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(left+Math.Max(0,(width-MaterialText.Measure(UiText.T(label)).X)*.5f));MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant,UiText.T(label));
            using(UiText.Font(number?UiFontRole.Title:UiFontRole.Heading))
            {
                ImGui.SetCursorPosX(left+Math.Max(0,(width-MaterialText.Measure(value).X)*.5f));
                ImGui.PushTextWrapPos(left+width);MaterialText.TextColored(color??MaterialTheme.Current.Colors.OnSurface,value);ImGui.PopTextWrapPos();
            }
        }
    }


    private void DrawComposer()
    {
        var configuration=plugin.Configuration;var changed=false;
        using var panel=new DhogGptPresentation.Panel("##DhogGptComposerPanel");
        if(!panel.Visible)return;
        var s=MaterialTheme.Metrics.Scale;
        using var composerGeometry=new MaterialStyleScope();
        if(configuration.UiCompact)
        {
            var colors=MaterialTheme.Current.Colors;
            composerGeometry.Color(ImGuiCol.FrameBg,colors.SurfaceContainer);
            composerGeometry.Color(ImGuiCol.FrameBgHovered,MaterialColor.Layer(colors.SurfaceContainer,colors.OnSurface,.08f));
        }
        composerGeometry.Style(ImGuiStyleVar.ItemSpacing,new Vector2(configuration.UiCompact?12:14,configuration.UiCompact?6:8)*s);
        composerGeometry.Style(ImGuiStyleVar.FramePadding,new Vector2(12*s,Math.Max(0,(40*s-ImGui.GetFontSize())*.5f)));
        composerGeometry.Style(ImGuiStyleVar.CellPadding,new Vector2(ImGui.GetStyle().CellPadding.X,0));
        DhogGptPresentation.Text("Outgoing translation",UiFontRole.Heading,MaterialTheme.Current.Colors.OnSurface,20);
        if(isMasterWindow)
        {
            using var channelGeometry=new MaterialStyleScope();
            channelGeometry.Style(ImGuiStyleVar.FramePadding,new Vector2(8*s,Math.Max(0,(24*s-ImGui.GetFontSize())*.5f)));
            var channelWidth=Math.Max(160*s,MaterialText.Measure(" "+GetOutgoingConversationDisplayLabel()).X+ImGui.GetFrameHeight()+16*s);
            var right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
            if(right-ImGui.GetItemRectMax().X>=channelWidth+ImGui.GetStyle().ItemSpacing.X)
            {
                ImGui.SameLine();ImGui.SetCursorPosX(ImGui.GetCursorPosX()+Math.Max(0,ImGui.GetContentRegionAvail().X-channelWidth));
            }
            ImGui.SetNextItemWidth(channelWidth);
            changed|=DrawOutgoingChannelCombo("Channel",showLabel:false);
            if(ImGui.IsItemHovered())UiGui.SetTooltip("Channel");
        }
        else MaterialText.TextDisabled(GetOutgoingConversationDisplayLabel());
        if(!configuration.UiCompact)ImGui.Separator();
        var controlsRoot=ImGui.GetID("");
        if(ImGui.BeginTable("DhogGptOutgoingLanguages",ImGui.GetContentRegionAvail().X<680*MaterialTheme.Metrics.Scale?1:2,ImGuiTableFlags.NoSavedSettings|ImGuiTableFlags.SizingStretchSame))
        {
            // The existing From/To fields retain their original window IDs across this presentation table.
            var originalRoot=controlsRoot;
            ImGui.TableNextColumn();ImGuiP.PushOverrideID(originalRoot);
            ImGui.AlignTextToFramePadding();UiGui.TextUnformatted("From (your language)");ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(80*MaterialTheme.Metrics.Scale,ImGui.GetContentRegionAvail().X));
            changed|=DrawLanguageCombo("From",configuration.OutgoingSourceLanguage,value=>configuration.OutgoingSourceLanguage=value,includeAuto:true,showLabel:false);
            if(ImGui.IsItemHovered())UiGui.SetTooltip("Your typed language before DhogGPT translates it.");
            ImGui.PopID();
            ImGui.TableNextColumn();ImGuiP.PushOverrideID(originalRoot);
            ImGui.AlignTextToFramePadding();UiGui.TextUnformatted("To (target language)");ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(80*MaterialTheme.Metrics.Scale,ImGui.GetContentRegionAvail().X));
            changed|=DrawLanguageCombo("To",configuration.OutgoingTargetLanguage,value=>configuration.OutgoingTargetLanguage=value,includeAuto:false,showLabel:false);
            if(ImGui.IsItemHovered())UiGui.SetTooltip("The language DhogGPT should translate your outgoing text into.");
            ImGui.PopID();ImGui.EndTable();
        }
        ImGui.SetCursorPosY(ImGui.GetCursorPosY()+(configuration.UiCompact?0:5)*s);
        DhogGptPresentation.Text("Message",UiFontRole.BodyStrong,MaterialTheme.Current.Colors.OnSurface,16);
        var currentDraft=outgoingDraft;
        using(var messageColors=new MaterialStyleScope())
        {
            var messagePalette=MaterialTheme.Current.Colors;
            messageColors.Color(ImGuiCol.FrameBg,configuration.UiCompact?messagePalette.SurfaceContainerLow:messagePalette.SurfaceContainerLowest);
            if(UiGui.InputTextMultiline("Message",ref currentDraft,2000,new Vector2(-1f,(configuration.UiCompact?88:120)*MaterialTheme.Metrics.Scale),showLabel:false))
            {
                outgoingDraft=currentDraft;if(isMasterWindow)configuration.OutgoingDraft=currentDraft;changed=true;
            }
        }
        var counterY=ImGui.GetCursorPosY();
        using(UiText.Font(UiFontRole.Body))
        {
            var count=UiText.F("{0} / 2000",outgoingDraft.Length);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX()+Math.Max(0,ImGui.GetContentRegionAvail().X-MaterialText.Measure(count).X));
            MaterialText.Text(count);
        }
        ImGui.SetCursorPosY(counterY+10*s);
        if(changed && isMasterWindow)configuration.Save();
        ImGui.BeginDisabled(previewBusy);
        var wide=ImGui.GetContentRegionAvail().X>=800*s;
        var width=wide?Math.Min((configuration.UiCompact?1004:978)*s,ImGui.GetContentRegionAvail().X):ImGui.GetContentRegionAvail().X;
        if(wide)ImGui.SetCursorPosX(ImGui.GetCursorPosX()+Math.Max(0,(ImGui.GetContentRegionAvail().X-width)*.5f-(configuration.UiCompact?12*s:0)));
        width=wide?width-ImGui.GetStyle().ItemSpacing.X*2:width;
        var previewWidth=wide?width*(configuration.UiCompact?.3194f:.3063158f):width;
        var sendWidth=wide?width*(configuration.UiCompact?.3806f:.3821053f):width;
        var clearWidth=wide?width-previewWidth-sendWidth:width;
        if(UiGui.Button("Preview translation",new Vector2(previewWidth,0),icon:MaterialIcon.Search))_=PreviewAsync(sendAfterTranslate:false);
        if(wide)ImGui.SameLine();
        if(DhogGptPresentation.PrimaryButton("Translate and send",new Vector2(sendWidth,0),MaterialIcon.Send))_=PreviewAsync(sendAfterTranslate:true);
        if(wide)ImGui.SameLine();
        if(UiGui.Button("Clear",new Vector2(clearWidth,0),icon:MaterialIcon.Delete))
        {
            outgoingDraft=string.Empty;if(isMasterWindow) { configuration.OutgoingDraft=string.Empty;configuration.Save(); }
            previewStatus=string.Empty;previewText=string.Empty;previewMetadata=string.Empty;
        }
        ImGui.EndDisabled();
        ImGui.Spacing();
        using(var previewPanel=new DhogGptPresentation.Panel("##DhogGptPreviewPanel",MaterialElevation.Low,configuration.UiCompact?8:17))
        {
            if(previewPanel.Visible)
            {
                DhogGptPresentation.Text("Translation preview",UiFontRole.Heading,MaterialTheme.Current.Colors.OnSurface,20);
                if(!string.IsNullOrWhiteSpace(previewStatus))UiGui.TextWrapped(UiText.Status(previewStatus));
                if(!string.IsNullOrWhiteSpace(previewMetadata))UiGui.TextWrapped(UiText.Status(previewMetadata));
                if(!string.IsNullOrWhiteSpace(previewText))
                    UiGui.InputTextMultiline("Translated preview",ref previewText,4000,new Vector2(-1f,(configuration.UiCompact?54:80)*s),ImGuiInputTextFlags.ReadOnly,showLabel:false);
                else
                {
                    var text=UiText.T("Preview will appear here.");
                    var min=ImGui.GetCursorScreenPos();var emptyPreviewWidth=ImGui.GetContentRegionAvail().X;
                    var wrap=Math.Max(ImGui.GetFontSize(),emptyPreviewWidth-16*s);
                    var textSize=MaterialText.Measure(text,false,wrap);
                    var previewHeight=Math.Max((configuration.UiCompact?42:64)*s,textSize.Y+16*s);
                    var drawing=ImGui.GetWindowDrawList();
                    var dash=6*s;var dashGap=4*s;var border=ImGui.GetColorU32(ImGuiCol.Border);
                    for(var x=0f;x<emptyPreviewWidth;x+=dash+dashGap)
                    {
                        var end=Math.Min(emptyPreviewWidth,x+dash);
                        drawing.AddLine(min+new Vector2(x,0),min+new Vector2(end,0),border);
                        drawing.AddLine(min+new Vector2(x,previewHeight),min+new Vector2(end,previewHeight),border);
                    }
                    for(var y=0f;y<previewHeight;y+=dash+dashGap)
                    {
                        var end=Math.Min(previewHeight,y+dash);
                        drawing.AddLine(min+new Vector2(0,y),min+new Vector2(0,end),border);
                        drawing.AddLine(min+new Vector2(emptyPreviewWidth,y),min+new Vector2(emptyPreviewWidth,end),border);
                    }
                    MaterialText.AddText(drawing,ImGui.GetFont(),ImGui.GetFontSize(),min+new Vector2((emptyPreviewWidth-textSize.X)*.5f,(previewHeight-textSize.Y)*.5f),
                        MaterialCanvas.Color(MaterialTheme.Current.Colors.OnSurfaceVariant),text,wrap);
                    ImGui.Dummy(new Vector2(emptyPreviewWidth,previewHeight));
                }
            }
        }
        if(isMasterWindow)DrawDirectMessageCreationPopup();
    }


    private async Task PreviewAsync(bool sendAfterTranslate)
    {
        if (previewBusy)
            return;

        previewBusy = true;
        previewStatus = string.Empty;
        previewText = string.Empty;
        previewMetadata = string.Empty;

        try
        {
            if (TryExtractRawSlashCommand(outgoingDraft, out _))
            {
                if (!sendAfterTranslate)
                {
                    await SetPreviewStateAsync(status: "Slash commands are sent directly and are not translated or logged.");
                    return;
                }

                await TryHandleRawSlashCommandAsync(simpleChatMode: false);
                return;
            }

            var retainedConversationSelection = CaptureRetainedConversationSelection();
            var request = BuildOutgoingRequest(recordInHistory: false);
            previewStatus = ShouldBypassOutgoingTranslation(request)
                ? (sendAfterTranslate ? "Sending without translation..." : "Previewing without translation...")
                : "Working...";
            var result = await TranslateOutgoingRequestAsync(request);
            if (!result.Success)
            {
                await SetPreviewStateAsync(status: $"Translation failed: {result.Error}");
                return;
            }

            var sourceDisplay = !string.IsNullOrWhiteSpace(result.DetectedSourceLanguage)
                ? languageRegistry.GetName(result.DetectedSourceLanguage)
                : languageRegistry.GetName(result.Request.SourceLanguage);

            await SetPreviewStateAsync(
                status: result.FromCache ? "Preview ready from cache." : "Preview ready.",
                text: result.TranslatedText,
                metadata: $"Source: {sourceDisplay}  Target: {languageRegistry.GetName(result.Request.TargetLanguage)}  Provider: {result.ProviderName} ({result.Endpoint})");

            if (!sendAfterTranslate)
                return;

            if (IsSafeConversation(request))
            {
                RecordSuccessfulSend(result);
                await Plugin.Framework.RunOnFrameworkThread(() => ClearOutgoingDraftAfterSuccessfulSend(clearLiveChatInput: false));

                RestoreConversationSelectionAfterSend(retainedConversationSelection, request);
                await SetPreviewStateAsync(status: "Saved to Safe.", text: result.TranslatedText);
                return;
            }

            if (!TryBuildOutgoingPrefixForActiveConversation(out var prefix, out var error))
            {
                await SetPreviewStateAsync(status: error);
                return;
            }

            if (!TryValidateOutgoingDirectMessageSend(out var sendValidationError))
            {
                await SetPreviewStateAsync(status: sendValidationError);
                return;
            }

            var sent = await Plugin.Framework.RunOnFrameworkThread(() => TrySendOutgoingTranslatedResult(result, prefix, out var sendError)
                ? (true, string.Empty)
                : (false, sendError));
            if (sent.Item1)
            {
                RecordSuccessfulSend(result);
                RestoreConversationSelectionAfterSend(retainedConversationSelection, request);
            }

            await SetPreviewStateAsync(status: sent.Item1
                ? $"Sent translated message to {GetOutgoingConversationDisplayLabel()}."
                : sent.Item2);
        }
        catch (OperationCanceledException)
        {
            await SetPreviewStateAsync(status: "Translation was cancelled.");
        }
        catch (Exception ex)
        {
            await SetPreviewStateAsync(status: $"Unexpected error: {ex.Message}");
            Plugin.Log.Error($"[DhogGPT] Preview/send failed: {ex.Message}");
        }
        finally
        {
            await SetPreviewStateAsync(busy: false);
        }
    }

    private async Task SendSimpleChatAsync()
    {
        if (previewBusy)
            return;

        previewBusy = true;
        simpleChatStatus = string.Empty;

        try
        {
            if (await TryHandleRawSlashCommandAsync(simpleChatMode: true))
                return;

            var retainedConversationSelection = CaptureRetainedConversationSelection();
            var request = BuildOutgoingRequest(recordInHistory: false);
            if (!ShouldBypassOutgoingTranslation(request))
                simpleChatStatus = "Translating...";

            var result = await TranslateOutgoingRequestAsync(request);
            if (!result.Success)
            {
                await SetSimpleChatStatusAsync($"Translation failed: {result.Error}");
                return;
            }

            if (IsSafeConversation(request))
            {
                RecordSuccessfulSend(result);

                await Plugin.Framework.RunOnFrameworkThread(() => ClearOutgoingDraftAfterSuccessfulSend(clearLiveChatInput: false));

                RestoreConversationSelectionAfterSend(retainedConversationSelection, request);
                await SetSimpleChatStatusAsync(string.Empty);
                return;
            }

            if (!TryBuildOutgoingPrefixForActiveConversation(out var prefix, out var error))
            {
                await SetSimpleChatStatusAsync(error);
                return;
            }

            if (!TryValidateOutgoingDirectMessageSend(out var sendValidationError))
            {
                await SetSimpleChatStatusAsync(sendValidationError);
                return;
            }

            var sent = await Plugin.Framework.RunOnFrameworkThread(() => TrySendOutgoingTranslatedResult(result, prefix, out var sendError)
                ? (true, string.Empty)
                : (false, sendError));
            if (!sent.Item1)
            {
                await SetSimpleChatStatusAsync(sent.Item2);
                return;
            }

            RecordSuccessfulSend(result);

            RestoreConversationSelectionAfterSend(retainedConversationSelection, request);
            await SetSimpleChatStatusAsync(string.Empty);
        }
        catch (OperationCanceledException)
        {
            await SetSimpleChatStatusAsync("Translation was cancelled.");
        }
        catch (Exception ex)
        {
            await SetSimpleChatStatusAsync($"Unexpected error: {ex.Message}");
            Plugin.Log.Error($"[DhogGPT] Simple chat send failed: {ex.Message}");
        }
        finally
        {
            await Plugin.Framework.RunOnFrameworkThread(() =>
            {
                previewBusy = false;
                requestSimpleComposerFocus = true;
            });
        }
    }

    private TranslationRequest BuildOutgoingRequest(bool recordInHistory)
    {
        TryAdoptCurrentPayloadDraft(forceOverwrite: string.IsNullOrWhiteSpace(outgoingDraft));
        ClearOutgoingPayloadDraftIfStale();

        var configuration = plugin.Configuration;
        if (!TryGetComposerConversation(out var conversationKey, out var conversationLabel, out var channelLabel))
        {
            conversationKey = "channel:ECHO";
            conversationLabel = "Safe";
            channelLabel = "Safe";
        }

        return new TranslationRequest
        {
            Text = outgoingDraft,
            OriginalSeStringBase64 = outgoingDraftOriginalSeStringBase64,
            SourceLanguage = configuration.OutgoingSourceLanguage,
            TargetLanguage = configuration.OutgoingTargetLanguage,
            IsInbound = false,
            ChannelLabel = channelLabel,
            Sender = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "You",
            ConversationKey = conversationKey,
            ConversationLabel = conversationLabel,
            RecordInHistory = recordInHistory,
        };
    }

    private (string Key, string Label) CaptureRetainedConversationSelection()
        => (activeConversationKey, activeConversationLabel);

    private void RestoreConversationSelectionAfterSend((string Key, string Label) retainedSelection, TranslationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(retainedSelection.Key) &&
            TryParseCombinedConversationKey(retainedSelection.Key, out var combinedConversationKeys) &&
            combinedConversationKeys.Any(conversationKey => conversationKey.Equals(request.ConversationKey, StringComparison.OrdinalIgnoreCase)))
        {
            activeConversationKey = retainedSelection.Key;
            activeConversationLabel = string.IsNullOrWhiteSpace(retainedSelection.Label)
                ? GetDefaultConversationLabel(retainedSelection.Key)
                : retainedSelection.Label;
            forceActiveConversationSelection = true;
            return;
        }

        activeConversationKey = request.ConversationKey;
        activeConversationLabel = request.ConversationLabel;
        forceActiveConversationSelection = true;
    }

    private bool TryGetComposerConversation(out string conversationKey, out string conversationLabel, out string channelLabel)
    {
        conversationKey = activeConversationKey;
        conversationLabel = activeConversationLabel;
        channelLabel = activeConversationLabel;

        if (string.IsNullOrWhiteSpace(conversationKey))
        {
            if (!isMasterWindow)
                return false;

            var configuredConversation = ChatChannelMapper.GetOutgoingConversation(plugin.Configuration);
            conversationKey = configuredConversation.Key;
            conversationLabel = configuredConversation.Label;
        }

        if (conversationKey.Equals(ChatChannelMapper.DirectMessageComposerKey, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(conversationLabel) &&
            TryResolveConversationTabState(conversationKey, out var resolvedConversation))
        {
            conversationLabel = resolvedConversation.Label;
        }

        if (IsDirectMessageConversation(conversationKey))
        {
            channelLabel = "DM";
            conversationLabel = ChatChannelMapper.NormalizeDirectMessageLabel(conversationLabel);
            return !string.IsNullOrWhiteSpace(conversationLabel) &&
                   !conversationLabel.Equals("DM", StringComparison.OrdinalIgnoreCase);
        }

        if (TryParseCombinedConversationKey(conversationKey, out var combinedConversationKeys) &&
            combinedConversationKeys.Count > 0)
        {
            conversationKey = combinedConversationKeys[0];
            conversationLabel = GetDisplayLabelForConversationKey(conversationKey);
        }

        if (string.IsNullOrWhiteSpace(conversationLabel))
            conversationLabel = GetDefaultConversationLabel(conversationKey);

        channelLabel = conversationLabel;
        return true;
    }

    private bool TryBuildOutgoingPrefixForActiveConversation(out string prefix, out string error)
    {
        prefix = string.Empty;
        error = string.Empty;

        if (!TryGetComposerConversation(out var conversationKey, out var conversationLabel, out _))
        {
            error = "Select a conversation before sending.";
            return false;
        }

        switch (conversationKey.ToUpperInvariant())
        {
            case "CHANNEL:ECHO":
                error = "Safe messages stay inside DhogGPT and are not sent to game chat.";
                return false;
            case "CHANNEL:ECHO_CHAT":
                prefix = "/echo ";
                break;
            case "CHANNEL:SAY":
                prefix = "/s ";
                break;
            case "CHANNEL:PARTY":
                prefix = "/p ";
                break;
            case "CHANNEL:ALLIANCE":
                prefix = "/a ";
                break;
            case "CHANNEL:PVP TEAM":
                prefix = "/pvpteam ";
                break;
            case "CHANNEL:FC":
                prefix = "/fc ";
                break;
            case "CHANNEL:SHOUT":
                prefix = "/sh ";
                break;
            case "CHANNEL:YELL":
                prefix = "/y ";
                break;
            case "CHANNEL:NN":
                prefix = "/beginner ";
                break;
            default:
                if (conversationKey.StartsWith("channel:LS", StringComparison.OrdinalIgnoreCase))
                {
                    prefix = $"/l{ParseConversationSlot(conversationKey, "channel:LS") ?? 1} ";
                    break;
                }

                if (conversationKey.StartsWith("channel:CWLS", StringComparison.OrdinalIgnoreCase))
                {
                    prefix = $"/cwl{ParseConversationSlot(conversationKey, "channel:CWLS") ?? 1} ";
                    break;
                }

                if (IsDirectMessageConversation(conversationKey))
                {
                    if (!ChatChannelMapper.TryNormalizeDirectMessageIdentity(conversationLabel, out var normalizedIdentity, out var directMessageError))
                    {
                        error = directMessageError;
                        return false;
                    }

                    prefix = $"/tell {normalizedIdentity} ";
                    break;
                }

                error = "Unsupported outgoing channel.";
                return false;
        }

        return true;
    }

    private bool TryBuildOutgoingCommandForActiveConversation(string translatedText, out string command, out string error)
    {
        command = string.Empty;
        if (!TryBuildOutgoingPrefixForActiveConversation(out var prefix, out error))
            return false;

        var trimmed = translatedText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "There is no translated text to send.";
            return false;
        }

        command = prefix + trimmed;
        if (Encoding.UTF8.GetByteCount(command) > 500)
        {
            error = "Translated message is too long for the game chat box.";
            command = string.Empty;
            return false;
        }

        return true;
    }

    private bool TryAdoptCurrentPayloadDraft(bool forceOverwrite)
    {
        if (!CommandHelper.TryCaptureCurrentPayloadDraft(out var plainText, out var originalSeStringBase64))
            return false;

        if (!forceOverwrite &&
            !string.IsNullOrWhiteSpace(outgoingDraft) &&
            !string.Equals(outgoingDraft, plainText, StringComparison.Ordinal))
        {
            return false;
        }

        outgoingDraft = plainText;
        outgoingDraftOriginalSeStringBase64 = originalSeStringBase64;
        outgoingDraftPayloadSourceText = plainText;
        if (isMasterWindow)
        {
            plugin.Configuration.OutgoingDraft = plainText;
            plugin.Configuration.Save();
        }

        return true;
    }

    private void ClearOutgoingPayloadDraftIfStale()
    {
        if (string.IsNullOrWhiteSpace(outgoingDraftOriginalSeStringBase64))
            return;

        if (string.Equals(outgoingDraft, outgoingDraftPayloadSourceText, StringComparison.Ordinal))
            return;

        outgoingDraftOriginalSeStringBase64 = string.Empty;
        outgoingDraftPayloadSourceText = string.Empty;
    }

    private void ClearOutgoingDraftAfterSuccessfulSend(bool clearLiveChatInput)
    {
        outgoingDraft = string.Empty;
        outgoingDraftOriginalSeStringBase64 = string.Empty;
        outgoingDraftPayloadSourceText = string.Empty;
        if (clearLiveChatInput)
            CommandHelper.ClearCurrentChatInput();
        if (isMasterWindow)
        {
            plugin.Configuration.OutgoingDraft = string.Empty;
            plugin.Configuration.Save();
        }
    }

    private bool TrySendOutgoingTranslatedResult(TranslationResult result, string prefix, out string error)
    {
        error = "Translation succeeded, but sending the message failed.";

        if (!string.IsNullOrWhiteSpace(result.Request.OriginalSeStringBase64))
        {
            var preservedPayloadText = PreservedPayloadTranslationText.Prepare(result.Request);
            if (!preservedPayloadText.HasPayloadBlocks ||
                !preservedPayloadText.TryBuildOutgoingSeStringBytes(prefix, result.TranslatedText.Trim(), out var bytes))
            {
                error = "Translation succeeded, but DhogGPT could not rebuild the item/map payload for sending.";
                return false;
            }

            var payloadSent = CommandHelper.SendEncodedSeString(bytes);
            if (payloadSent)
                ClearOutgoingDraftAfterSuccessfulSend(clearLiveChatInput: true);
            else
                error = "Translation succeeded, but the payload-preserving send path failed.";

            return payloadSent;
        }

        if (!TryBuildOutgoingCommandForActiveConversation(result.TranslatedText, out var command, out error))
            return false;

        var sent = CommandHelper.SendCommand(command);
        if (sent)
            ClearOutgoingDraftAfterSuccessfulSend(clearLiveChatInput: false);

        return sent;
    }

    private static string GetDefaultConversationLabel(string conversationKey)
    {
        if (TryParseCombinedConversationKey(conversationKey, out var combinedConversationKeys))
            return BuildDefaultCombinedConversationLabel(combinedConversationKeys);

        return conversationKey.ToUpperInvariant() switch
        {
            "CHANNEL:ECHO" => "Safe",
            "CHANNEL:ECHO_CHAT" => "Echo",
            "CHANNEL:PROGRESS" => "System",
            "CHANNEL:COMBAT" => "Events",
            "CHANNEL:SAY" => "Say",
            "CHANNEL:EMOTE" => "Emote",
            "CHANNEL:PARTY" => "Party",
            "CHANNEL:ALLIANCE" => "Alliance",
            "CHANNEL:PVP TEAM" => "PvP Team",
            "CHANNEL:FC" => "FC",
            "CHANNEL:SHOUT" => "Shout",
            "CHANNEL:YELL" => "Yell",
            "CHANNEL:NN" => "NN",
            _ when conversationKey.StartsWith("channel:LS", StringComparison.OrdinalIgnoreCase) => conversationKey["channel:".Length..].ToUpperInvariant(),
            _ when conversationKey.StartsWith("channel:CWLS", StringComparison.OrdinalIgnoreCase) => conversationKey["channel:".Length..].ToUpperInvariant(),
            _ => "Chat",
        };
    }

    private static bool TryExtractRawSlashCommand(string draft, out string command)
    {
        command = draft.Trim();
        return !string.IsNullOrWhiteSpace(command) &&
               command.StartsWith("/", StringComparison.Ordinal);
    }

    private async Task<bool> TryHandleRawSlashCommandAsync(bool simpleChatMode)
    {
        if (!TryExtractRawSlashCommand(outgoingDraft, out var command))
            return false;

        var sent = await Plugin.Framework.RunOnFrameworkThread(() => CommandHelper.SendCommand(command));
        if (sent)
        {
            await Plugin.Framework.RunOnFrameworkThread(() =>
            {
                RecordSlashCommandEcho(command);
                outgoingDraft = string.Empty;
                if (isMasterWindow)
                {
                    plugin.Configuration.OutgoingDraft = string.Empty;
                    plugin.Configuration.Save();
                }
            });
        }

        if (simpleChatMode)
            await SetSimpleChatStatusAsync(sent ? string.Empty : "Slash command failed to send.");
        else
            await SetPreviewStateAsync(status: sent ? "Slash command sent directly." : "Slash command failed to send.");

        return true;
    }

    private Task<TranslationResult> TranslateOutgoingRequestAsync(TranslationRequest request)
    {
        if (!ShouldBypassOutgoingTranslation(request))
            return translationCoordinator.TranslateImmediatelyAsync(request);

        var passthroughText = request.Text.Trim();
        return Task.FromResult(TranslationResult.Succeeded(
            request,
            passthroughText,
            "NoTranslation",
            "SameLanguageBypass",
            request.SourceLanguage,
            TimeSpan.Zero,
            fromCache: true));
    }

    private Task SetPreviewStateAsync(string? status = null, string? text = null, string? metadata = null, bool? busy = null)
        => Plugin.Framework.RunOnFrameworkThread(() =>
        {
            if (busy.HasValue)
                previewBusy = busy.Value;
            if (status != null)
                previewStatus = status;
            if (text != null)
                previewText = text;
            if (metadata != null)
                previewMetadata = metadata;
        });

    private Task SetSimpleChatStatusAsync(string status)
        => Plugin.Framework.RunOnFrameworkThread(() => simpleChatStatus = status);

    private void ClearTransientUiStatus()
    {
        if (string.IsNullOrWhiteSpace(simpleChatStatus))
            return;

        simpleChatStatus = string.Empty;
    }

    private void DrawSimpleChatStatusBanner()
    {
        if (string.IsNullOrWhiteSpace(simpleChatStatus))
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.74f, 0.74f, 1.0f));
        UiGui.TextWrapped(UiText.Status(simpleChatStatus));
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void EnsureUltraCompactLanguageDefaults()
    {
        var configuration = plugin.Configuration;
        if (string.Equals(configuration.OutgoingSourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            configuration.OutgoingSourceLanguage = string.Equals(configuration.IncomingTargetLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : configuration.IncomingTargetLanguage;
        }

        if (string.Equals(configuration.OutgoingTargetLanguage, "auto", StringComparison.OrdinalIgnoreCase))
            configuration.OutgoingTargetLanguage = "en";

        configuration.IncomingSourceLanguage = "auto";
        configuration.IncomingTargetLanguage = configuration.OutgoingSourceLanguage;
    }

    private static TranslationResult CreateRecordedResult(TranslationResult result)
    {
        if (result.Request.RecordInHistory)
            return result;

        var recordedRequest = new TranslationRequest
        {
            Text = result.Request.Text,
            OriginalSeStringBase64 = result.Request.OriginalSeStringBase64,
            SourceLanguage = result.Request.SourceLanguage,
            TargetLanguage = result.Request.TargetLanguage,
            IsInbound = result.Request.IsInbound,
            Sender = result.Request.Sender,
            ChannelLabel = result.Request.ChannelLabel,
            ConversationKey = result.Request.ConversationKey,
            ConversationLabel = result.Request.ConversationLabel,
            RecordInHistory = true,
            RequestedAtUtc = result.Request.RequestedAtUtc,
        };

        return result.Success
            ? TranslationResult.Succeeded(
                recordedRequest,
                result.TranslatedText,
                result.ProviderName,
                result.Endpoint,
                result.DetectedSourceLanguage,
                result.Duration,
                result.FromCache)
            : TranslationResult.Failed(
                recordedRequest,
                result.ProviderName,
                result.Endpoint,
                result.Error,
                result.Duration);
    }

    private void RecordSuccessfulSend(TranslationResult result)
    {
        var recordedResult = CreateRecordedResult(result);
        if (recordedResult.Request.ChannelLabel.Equals("DM", StringComparison.OrdinalIgnoreCase))
        {
            ChatChannelMapper.RegisterKnownDirectMessageIdentity(recordedResult.Request.ConversationLabel);
            plugin.ChatTranslationService.RegisterPendingOutgoingDirectMessage(recordedResult);
            return;
        }

        translationCoordinator.RecordTranslationResult(recordedResult);
    }

    private bool TryValidateOutgoingDirectMessageSend(out string error)
    {
        error = string.Empty;
        if (!TryGetComposerConversation(out var conversationKey, out _, out _) ||
            !IsDirectMessageConversation(conversationKey))
        {
            return true;
        }

        if (!Plugin.Condition[ConditionFlag.OnFreeTrial])
            return true;

        error = "Free trial accounts cannot send DMs.";
        return false;
    }

    private static bool ShouldBypassOutgoingTranslation(TranslationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return false;

        if (string.IsNullOrWhiteSpace(request.SourceLanguage) ||
            string.IsNullOrWhiteSpace(request.TargetLanguage) ||
            string.Equals(request.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(request.SourceLanguage, request.TargetLanguage, StringComparison.OrdinalIgnoreCase);
    }

    private bool DrawOutgoingChannelCombo(string label,bool showLabel=true)
    {
        var changed = false;
        var selectedLabel = GetOutgoingConversationDisplayLabel();

        if (!UiGui.BeginCombo(label, selectedLabel,showLabel:showLabel))
            return false;

        suppressSimpleComposerAutoFocusThisFrame = true;

        foreach (var conversation in GetPinnedGeneralConversations())
            changed |= DrawOutgoingConversationSelectable(conversation);

        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.95f, 0.55f, 1.0f));
        if (UiGui.Selectable("New DM", false))
            QueueOpenDirectMessagePopup();
        ImGui.PopStyleColor();

        ImGui.EndCombo();
        return changed;
    }

    private bool DrawLanguageCombo(string label, string currentCode, Action<string> setter, bool includeAuto, bool showLabel=true)
    {
        var changed = false;
        var options = includeAuto ? languageRegistry.GetSourceLanguages() : languageRegistry.GetTargetLanguages();
        var displayName = languageRegistry.GetName(currentCode);

        if (UiGui.BeginCombo(label, displayName, showLabel:showLabel))
        {
            suppressSimpleComposerAutoFocusThisFrame = true;
            foreach (var option in options)
            {
                var isSelected = option.Code.Equals(currentCode, StringComparison.OrdinalIgnoreCase);
                if (UiGui.Selectable(option.Name, isSelected))
                {
                    ClearTransientUiStatus();
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

    private string GetDisplayName(TranslationHistoryItem message)
    {
        if (string.IsNullOrWhiteSpace(message.Sender))
            return UiText.T(message.IsInbound ? "Unknown" : "You");
        var displayName = !string.IsNullOrWhiteSpace(message.Sender)
            ? message.Sender
            : message.IsInbound ? "Unknown" : "You";

        if (!plugin.Configuration.KrangleChatNames)
            return displayName;

        if (displayName.Equals("You", StringComparison.OrdinalIgnoreCase) ||
            displayName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return displayName;
        }

        return KrangleService.KrangleName(displayName);
    }

    private static string Normalize(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private string GetConversationDisplayLabel(ConversationTabState conversation)
    {
        if (TryParseCombinedConversationKey(conversation.Key, out _))
            return string.IsNullOrWhiteSpace(conversation.Label) ? GetDefaultConversationLabel(conversation.Key) : conversation.Label;

        if (!IsDirectMessageConversation(conversation.Key))
            return ShellChannelDisplayService.GetDisplayLabel(plugin.Configuration, conversation.Key, conversation.Label);

        if (!plugin.Configuration.KrangleChatNames)
            return conversation.Label;

        if (!conversation.Key.StartsWith("dm:", StringComparison.OrdinalIgnoreCase))
            return conversation.Label;

        return KrangleService.KrangleName(conversation.Label);
    }

    private string GetDisplayLabelForConversationKey(string conversationKey)
    {
        foreach (var conversation in GetAllGeneralConversations())
        {
            if (conversation.Key.Equals(conversationKey, StringComparison.OrdinalIgnoreCase))
                return GetConversationDisplayLabel(conversation);
        }

        return GetDefaultConversationLabel(conversationKey);
    }

    private string BuildCombinedConversationLabel(IEnumerable<string> conversationKeys)
    {
        var tokens = conversationKeys
            .Select(GetDisplayLabelForConversationKey)
            .Select(label => string.IsNullOrWhiteSpace(label) ? "?" : label.Trim()[0].ToString())
            .ToList();
        return tokens.Count == 0 ? "?" : string.Join("|", tokens);
    }

    private string GetConversationGroupingKey(TranslationHistoryItem entry)
    {
        if (string.Equals(entry.ChannelLabel, "DM", StringComparison.OrdinalIgnoreCase))
        {
            var identity = !string.IsNullOrWhiteSpace(entry.ConversationLabel)
                ? entry.ConversationLabel
                : entry.Sender;
            return ChatChannelMapper.GetDirectMessageConversationKey(identity);
        }

        if (!string.IsNullOrWhiteSpace(entry.ConversationKey))
            return entry.ConversationKey;

        return $"channel:{Normalize(entry.ChannelLabel).ToUpperInvariant()}";
    }

    private static string ResolveConversationLabel(IReadOnlyList<TranslationHistoryItem> messages)
    {
        var candidate = messages
            .Select(message => !string.IsNullOrWhiteSpace(message.ConversationLabel) ? message.ConversationLabel : message.Sender)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .OrderByDescending(label => label.Contains('@'))
            .ThenByDescending(label => label.Length)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate;

        return messages.FirstOrDefault()?.ChannelLabel ?? "Chat";
    }

    private bool ShouldInsertConfiguredConversation((string Key, string Label) configuredConversation)
    {
        if (!ShouldWindowDisplayConversation(configuredConversation.Key))
            return false;

        if (!IsDirectMessageConversation(configuredConversation.Key))
            return !configuredConversation.Key.Equals(ChatChannelMapper.DirectMessageComposerKey, StringComparison.OrdinalIgnoreCase) &&
                   !IsGeneralConversationCoveredByCombinedTab(configuredConversation.Key) &&
                   !IsGeneralConversationHidden(configuredConversation.Key);

        return !string.IsNullOrWhiteSpace(configuredConversation.Label) &&
               !configuredConversation.Label.Equals("New DM", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsConversationVisible(ConversationTabState conversation)
    {
        if (!IsDirectMessageConversation(conversation.Key))
        {
            closedConversationCutoffs.Remove(conversation.Key);
            return true;
        }

        if (pendingDirectMessageTabs.ContainsKey(conversation.Key) || IsPinnedDirectMessageConversation(conversation.Key))
        {
            closedConversationCutoffs.Remove(conversation.Key);
            return true;
        }

        if (conversation.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase))
        {
            closedConversationCutoffs.Remove(conversation.Key);
            return true;
        }

        if (!closedConversationCutoffs.TryGetValue(conversation.Key, out var cutoff))
            return true;

        if (conversation.LastMessageUtc > cutoff)
        {
            closedConversationCutoffs.Remove(conversation.Key);
            return true;
        }

        return false;
    }

    private void ApplyDefaultDirectMessageVisibility(string logIdentity, IReadOnlyList<ConversationTabState> directMessageConversations)
    {
        if (string.IsNullOrWhiteSpace(logIdentity) ||
            string.Equals(restoredDirectMessageLogIdentity, logIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        restoredDirectMessageLogIdentity = logIdentity;

        foreach (var existingKey in closedConversationCutoffs.Keys.Where(IsDirectMessageConversation).ToList())
            closedConversationCutoffs.Remove(existingKey);

        var autoClosedConversations = directMessageConversations
            .Where(conversation => !pendingDirectMessageTabs.ContainsKey(conversation.Key))
            .Where(conversation => !IsPinnedDirectMessageConversation(conversation.Key));

        foreach (var conversation in autoClosedConversations)
            closedConversationCutoffs[conversation.Key] = conversation.LastMessageUtc;
    }

    private void CloseConversation(ConversationTabState conversation, bool isPinnedDirectMessage)
    {
        if (!isMasterWindow)
        {
            plugin.CloseDetachedConversationWindow(conversationWindowId);
            return;
        }

        if (isPinnedDirectMessage && !ImGui.GetIO().KeyCtrl)
        {
            simpleChatStatus = "Pinned DM tabs require Ctrl while clicking x to close.";
            return;
        }

        pendingDirectMessageTabs.Remove(conversation.Key);
        closedConversationCutoffs[conversation.Key] = conversation.LastMessageUtc;
        if (conversation.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase))
        {
            activeConversationKey = string.Empty;
            activeConversationLabel = string.Empty;
        }
    }

    private void CloseGeneralConversation(ConversationTabState conversation)
    {
        if (!isMasterWindow)
        {
            plugin.CloseDetachedConversationWindow(conversationWindowId);
            return;
        }

        if (TryParseCombinedConversationKey(conversation.Key, out _))
        {
            ClearTransientUiStatus();
            ReleaseCombinedConversation(conversation.Key);
            return;
        }

        if (!ImGui.GetIO().KeyCtrl)
        {
            simpleChatStatus = "Hold Ctrl while clicking x to hide a channel tab.";
            return;
        }

        var visibleGeneralCount = GetPinnedGeneralConversations().Count;
        if (visibleGeneralCount <= 1)
        {
            simpleChatStatus = "At least one channel tab must remain visible.";
            return;
        }

        SetGeneralConversationHidden(conversation.Key, true);
        if (conversation.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase))
        {
            activeConversationKey = GetPinnedGeneralConversations().FirstOrDefault()?.Key ?? string.Empty;
            activeConversationLabel = GetPinnedGeneralConversations().FirstOrDefault()?.Label ?? string.Empty;
            forceActiveConversationSelection = true;
        }
    }

    private bool SyncSimpleComposerToActiveConversation()
    {
        if (!isMasterWindow)
            return false;

        if (!IsDirectMessageConversation(activeConversationKey) ||
            string.IsNullOrWhiteSpace(activeConversationLabel) ||
            string.Equals(activeConversationLabel, "New DM", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var configuration = plugin.Configuration;
        var desiredTarget = activeConversationLabel;
        if (configuration.SelectedOutgoingChannel == OutgoingChannel.Tell &&
            string.Equals(configuration.TellTarget, desiredTarget, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        configuration.SelectedOutgoingChannel = OutgoingChannel.Tell;
        configuration.TellTarget = desiredTarget;
        return true;
    }

    private void SyncActiveConversationToOutgoingChannel()
    {
        if (!isMasterWindow)
            return;

        var conversation = ChatChannelMapper.GetOutgoingConversation(plugin.Configuration);
        if (conversation.Key.Equals(ChatChannelMapper.DirectMessageComposerKey, StringComparison.OrdinalIgnoreCase))
        {
            QueueOpenDirectMessagePopup(plugin.Configuration.TellTarget);
            return;
        }

        activeConversationKey = conversation.Key;
        activeConversationLabel = conversation.Label;
        forceActiveConversationSelection = true;
    }

    private bool SyncOutgoingChannelToConversation(ConversationTabState conversation)
    {
        activeConversationKey = conversation.Key;
        activeConversationLabel = conversation.Label;

        if (!isMasterWindow)
            return false;

        var configuration = plugin.Configuration;
        if (conversation.Key.Equals(ChatChannelMapper.DirectMessageComposerKey, StringComparison.OrdinalIgnoreCase))
        {
            QueueOpenDirectMessagePopup();
            return false;
        }

        if (IsDirectMessageConversation(conversation.Key))
        {
            if (configuration.SelectedOutgoingChannel == OutgoingChannel.Tell &&
                string.Equals(configuration.TellTarget, conversation.Label, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            configuration.SelectedOutgoingChannel = OutgoingChannel.Tell;
            configuration.TellTarget = conversation.Label;
            return true;
        }

        return TryApplyConversationKeyToOutgoingChannel(conversation.Key);
    }

    private bool TryApplyConversationKeyToOutgoingChannel(string conversationKey)
    {
        if (conversationKey.Equals(ChatChannelMapper.DirectMessageComposerKey, StringComparison.OrdinalIgnoreCase))
        {
            QueueOpenDirectMessagePopup(isMasterWindow ? plugin.Configuration.TellTarget : null);
            return false;
        }

        var outgoingConversationKey = conversationKey;
        if (TryParseCombinedConversationKey(conversationKey, out var combinedConversationKeys) &&
            combinedConversationKeys.Count > 0)
        {
            outgoingConversationKey = combinedConversationKeys[0];
        }

        var label = GetDefaultConversationLabel(conversationKey);
        if (TryResolveConversationTabState(conversationKey, out var resolvedConversation))
            label = resolvedConversation.Label;

        var changed = !conversationKey.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase) ||
                      !label.Equals(activeConversationLabel, StringComparison.OrdinalIgnoreCase);
        activeConversationKey = conversationKey;
        activeConversationLabel = label;
        forceActiveConversationSelection = true;

        if (!isMasterWindow)
            return changed;

        var configuration = plugin.Configuration;
        switch (outgoingConversationKey)
        {
            case "channel:ECHO":
                return SetOutgoingChannel(configuration, OutgoingChannel.Safe) || changed;
            case "channel:ECHO_CHAT":
                return SetOutgoingChannel(configuration, OutgoingChannel.Echo) || changed;
            case "channel:SAY":
                return SetOutgoingChannel(configuration, OutgoingChannel.Say) || changed;
            case "channel:PARTY":
                return SetOutgoingChannel(configuration, OutgoingChannel.Party) || changed;
            case "channel:ALLIANCE":
                return SetOutgoingChannel(configuration, OutgoingChannel.Alliance) || changed;
            case "channel:PVP TEAM":
                return SetOutgoingChannel(configuration, OutgoingChannel.PvPTeam) || changed;
            case "channel:FC":
                return SetOutgoingChannel(configuration, OutgoingChannel.FreeCompany) || changed;
            case "channel:SHOUT":
                return SetOutgoingChannel(configuration, OutgoingChannel.Shout) || changed;
            case "channel:YELL":
                return SetOutgoingChannel(configuration, OutgoingChannel.Yell) || changed;
            case "channel:NN":
                return SetOutgoingChannel(configuration, OutgoingChannel.NoviceNetwork) || changed;
        }

        if (outgoingConversationKey.StartsWith("channel:LS", StringComparison.OrdinalIgnoreCase))
        {
            var slot = ParseConversationSlot(outgoingConversationKey, "channel:LS");
            var configurationChanged = SetOutgoingChannel(configuration, OutgoingChannel.Linkshell);
            if (slot.HasValue && configuration.LinkshellSlot != slot.Value)
            {
                configuration.LinkshellSlot = slot.Value;
                configurationChanged = true;
            }

            return configurationChanged || changed;
        }

        if (outgoingConversationKey.StartsWith("channel:CWLS", StringComparison.OrdinalIgnoreCase))
        {
            var slot = ParseConversationSlot(outgoingConversationKey, "channel:CWLS");
            var configurationChanged = SetOutgoingChannel(configuration, OutgoingChannel.CrossWorldLinkshell);
            if (slot.HasValue && configuration.CrossWorldLinkshellSlot != slot.Value)
            {
                configuration.CrossWorldLinkshellSlot = slot.Value;
                configurationChanged = true;
            }

            return configurationChanged || changed;
        }

        return changed;
    }

    private static bool SetOutgoingChannel(Configuration configuration, OutgoingChannel channel)
    {
        if (configuration.SelectedOutgoingChannel == channel)
            return false;

        configuration.SelectedOutgoingChannel = channel;
        return true;
    }

    private static int? ParseConversationSlot(string conversationKey, string prefix)
    {
        if (!conversationKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(conversationKey[prefix.Length..], out var slot)
            ? Math.Clamp(slot, 1, 8)
            : null;
    }

    private List<ConversationTabState> GetPinnedGeneralConversations()
    {
        var visibleGeneralConversations = GetAllGeneralConversations()
            .Where(conversation => !IsGeneralConversationHidden(conversation.Key))
            .Where(conversation => ShouldWindowDisplayConversation(conversation.Key))
            .ToList();

        var groupedConversationLookup = new Dictionary<string, ConversationTabState>(StringComparer.OrdinalIgnoreCase);
        var coveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var groupedConversationKeys in GetNormalizedCombinedGeneralConversationSpecs())
        {
            if (groupedConversationKeys.Count < 2)
                continue;

            var memberConversations = new List<ConversationTabState>();
            var skipGroup = false;
            foreach (var groupedConversationKey in groupedConversationKeys)
            {
                var match = visibleGeneralConversations.FirstOrDefault(conversation =>
                    conversation.Key.Equals(groupedConversationKey, StringComparison.OrdinalIgnoreCase));
                if (match is null || coveredKeys.Contains(groupedConversationKey))
                {
                    skipGroup = true;
                    break;
                }

                memberConversations.Add(match);
            }

            if (skipGroup || memberConversations.Count != groupedConversationKeys.Count)
                continue;

            foreach (var memberConversation in memberConversations)
                coveredKeys.Add(memberConversation.Key);

            groupedConversationLookup[groupedConversationKeys[0]] = new ConversationTabState(
                BuildCombinedConversationKey(groupedConversationKeys),
                BuildCombinedConversationLabel(groupedConversationKeys),
                Array.Empty<TranslationHistoryItem>(),
                DateTimeOffset.MinValue);
        }

        var output = new List<ConversationTabState>();
        foreach (var conversation in visibleGeneralConversations)
        {
            if (coveredKeys.Contains(conversation.Key))
            {
                if (groupedConversationLookup.Remove(conversation.Key, out var groupedConversation))
                    output.Add(groupedConversation);

                continue;
            }

            output.Add(conversation);
        }

        return output;
    }

    private List<ConversationTabState> GetAllGeneralConversations()
    {
        var configuration = plugin.Configuration;
        var conversations = new List<ConversationTabState>
        {
            BuildPinnedConversation("channel:ECHO", "Safe"),
            BuildPinnedConversation("channel:ECHO_CHAT", "Echo"),
            BuildPinnedConversation("channel:PROGRESS", "System"),
            BuildPinnedConversation("channel:COMBAT", "Events"),
            BuildPinnedConversation("channel:SAY", "Say"),
            BuildPinnedConversation("channel:EMOTE", "Emote"),
            BuildPinnedConversation("channel:PARTY", "Party"),
            BuildPinnedConversation("channel:ALLIANCE", "Alliance"),
            BuildPinnedConversation("channel:PVP TEAM", "PvP Team"),
            BuildPinnedConversation("channel:FC", "FC"),
            BuildPinnedConversation("channel:SHOUT", "Shout"),
            BuildPinnedConversation("channel:YELL", "Yell"),
            BuildPinnedConversation("channel:NN", "NN"),
        };

        if (configuration.EnableLinkshells)
        {
            conversations.AddRange(ShellChannelDisplayService
                .GetLinkshellChannels()
                .Select(channel => BuildPinnedConversation(channel.Key, channel.GetDisplayLabel(configuration))));
        }

        if (configuration.EnableCrossWorldLinkshells)
        {
            conversations.AddRange(ShellChannelDisplayService
                .GetCrossWorldLinkshellChannels()
                .Select(channel => BuildPinnedConversation(channel.Key, channel.GetDisplayLabel(configuration))));
        }

        return conversations;
    }

    private List<List<string>> GetNormalizedCombinedGeneralConversationSpecs()
    {
        var validGeneralKeys = GetAllGeneralConversations()
            .Select(conversation => conversation.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<List<string>>();

        foreach (var spec in plugin.Configuration.CombinedGeneralConversationSpecs)
        {
            if (!TryParseCombinedConversationSpec(spec, out var parsedKeys))
                continue;

            var normalizedKeys = new List<string>();
            var skipGroup = false;
            foreach (var parsedKey in parsedKeys)
            {
                if (!validGeneralKeys.Contains(parsedKey) || usedKeys.Contains(parsedKey))
                {
                    skipGroup = true;
                    break;
                }

                normalizedKeys.Add(parsedKey);
            }

            if (skipGroup || normalizedKeys.Count < 2)
                continue;

            output.Add(normalizedKeys);
            foreach (var normalizedKey in normalizedKeys)
                usedKeys.Add(normalizedKey);
        }

        return output;
    }

    private bool IsGeneralConversationCoveredByCombinedTab(string conversationKey)
        => GetNormalizedCombinedGeneralConversationSpecs()
            .Any(group => group.Any(item => item.Equals(conversationKey, StringComparison.OrdinalIgnoreCase)));

    private List<ConversationTabState> GetAvailableCombineTargets(string conversationKey)
    {
        var currentGroupKeys = GetGeneralConversationKeysForCombination(conversationKey);
        if (currentGroupKeys.Count == 0)
            return [];

        var reservedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var groupedConversationKeys in GetNormalizedCombinedGeneralConversationSpecs())
        {
            var isCurrentGroup = groupedConversationKeys.Count == currentGroupKeys.Count &&
                                 !groupedConversationKeys.Except(currentGroupKeys, StringComparer.OrdinalIgnoreCase).Any();
            if (isCurrentGroup)
                continue;

            foreach (var groupedConversationKey in groupedConversationKeys)
                reservedKeys.Add(groupedConversationKey);
        }

        return GetAllGeneralConversations()
            .Where(conversation => !IsGeneralConversationHidden(conversation.Key))
            .Where(conversation => ShouldWindowDisplayConversation(conversation.Key))
            .Where(conversation => !currentGroupKeys.Any(key => key.Equals(conversation.Key, StringComparison.OrdinalIgnoreCase)))
            .Where(conversation => !reservedKeys.Contains(conversation.Key))
            .ToList();
    }

    private bool CombineGeneralConversation(string anchorConversationKey, string targetConversationKey)
    {
        var combinedConversationKeys = GetGeneralConversationKeysForCombination(anchorConversationKey);
        if (combinedConversationKeys.Count == 0 ||
            string.IsNullOrWhiteSpace(targetConversationKey) ||
            combinedConversationKeys.Any(key => key.Equals(targetConversationKey, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var normalizedSpecs = plugin.Configuration.CombinedGeneralConversationSpecs;
        normalizedSpecs.RemoveAll(spec => CombinedConversationSpecMatches(spec, combinedConversationKeys));

        combinedConversationKeys.Add(targetConversationKey);
        normalizedSpecs.Add(string.Join(CombinedConversationSeparator, combinedConversationKeys));
        activeConversationKey = BuildCombinedConversationKey(combinedConversationKeys);
        activeConversationLabel = BuildCombinedConversationLabel(combinedConversationKeys);
        forceActiveConversationSelection = true;
        plugin.Configuration.Save();
        return true;
    }

    private bool ReleaseCombinedConversation(string conversationKey)
    {
        if (!TryParseCombinedConversationKey(conversationKey, out var groupedConversationKeys) ||
            groupedConversationKeys.Count < 2)
        {
            return false;
        }

        var changed = plugin.Configuration.CombinedGeneralConversationSpecs.RemoveAll(spec =>
            CombinedConversationSpecMatches(spec, groupedConversationKeys)) > 0;
        if (!changed)
            return false;

        activeConversationKey = groupedConversationKeys[0];
        activeConversationLabel = GetDisplayLabelForConversationKey(activeConversationKey);
        forceActiveConversationSelection = true;
        plugin.Configuration.Save();
        return true;
    }

    private static string BuildCombinedConversationKey(IEnumerable<string> conversationKeys)
        => $"{CombinedConversationPrefix}{string.Join(CombinedConversationSeparator, conversationKeys)}";

    private static List<string> GetGeneralConversationKeysForCombination(string conversationKey)
    {
        if (TryParseCombinedConversationKey(conversationKey, out var groupedConversationKeys))
            return groupedConversationKeys;

        return string.IsNullOrWhiteSpace(conversationKey) || IsDirectMessageConversation(conversationKey)
            ? []
            : [conversationKey];
    }

    private static bool TryParseCombinedConversationSpec(string? spec, out List<string> conversationKeys)
    {
        conversationKeys = (spec ?? string.Empty)
            .Split(CombinedConversationSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return conversationKeys.Count >= 2;
    }

    private static bool TryParseCombinedConversationKey(string conversationKey, out List<string> conversationKeys)
    {
        if (!conversationKey.StartsWith(CombinedConversationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            conversationKeys = [];
            return false;
        }

        return TryParseCombinedConversationSpec(conversationKey[CombinedConversationPrefix.Length..], out conversationKeys);
    }

    private static bool CombinedConversationSpecMatches(string spec, IReadOnlyList<string> expectedConversationKeys)
        => TryParseCombinedConversationSpec(spec, out var parsedConversationKeys) &&
           parsedConversationKeys.Count == expectedConversationKeys.Count &&
           !parsedConversationKeys.Where((parsedConversationKey, index) =>
                   !parsedConversationKey.Equals(expectedConversationKeys[index], StringComparison.OrdinalIgnoreCase))
               .Any();

    private static string BuildDefaultCombinedConversationLabel(IEnumerable<string> conversationKeys)
    {
        var tokens = conversationKeys
            .Select(GetDefaultConversationLabel)
            .Select(label => string.IsNullOrWhiteSpace(label) ? "?" : label.Trim()[0].ToString())
            .ToList();
        return tokens.Count == 0 ? "?" : string.Join("|", tokens);
    }

    private static ConversationTabState BuildPinnedConversation(string key, string label)
        => new(key, label, new List<TranslationHistoryItem>(), DateTimeOffset.MinValue);

    private bool IsGeneralConversationHidden(string conversationKey)
        => plugin.Configuration.HiddenGeneralConversationKeys.Any(hidden => string.Equals(hidden, conversationKey, StringComparison.OrdinalIgnoreCase));

    private void SetGeneralConversationHidden(string conversationKey, bool hidden)
    {
        ClearTransientUiStatus();
        var hiddenKeys = plugin.Configuration.HiddenGeneralConversationKeys;
        hiddenKeys.RemoveAll(existing => string.Equals(existing, conversationKey, StringComparison.OrdinalIgnoreCase));
        if (hidden)
            hiddenKeys.Add(conversationKey);

        plugin.Configuration.Save();
    }

    public void OpenDirectMessageConversation(string identity)
    {
        if (!ChatChannelMapper.TryNormalizeDirectMessageIdentity(identity, out var normalizedIdentity, out _))
            return;

        ClearTransientUiStatus();
        ChatChannelMapper.RegisterKnownDirectMessageIdentity(normalizedIdentity);
        if (isMasterWindow)
        {
            plugin.Configuration.SelectedOutgoingChannel = OutgoingChannel.Tell;
            plugin.Configuration.TellTarget = normalizedIdentity;
            plugin.Configuration.Save();
        }

        var conversationKey = ChatChannelMapper.GetDirectMessageConversationKey(normalizedIdentity);
        pendingDirectMessageTabs[conversationKey] = normalizedIdentity;
        closedConversationCutoffs.Remove(conversationKey);
        activeConversationKey = conversationKey;
        activeConversationLabel = normalizedIdentity;
        forceActiveConversationSelection = true;
        requestWindowFocus = true;
        requestSimpleComposerFocus = true;
        if (isMasterWindow)
            ApplySavedPositionForCurrentCharacter();

        IsOpen = true;
    }

    private void HandlePendingPopups()
    {
        if (!isMasterWindow)
            return;

        if (requestOpenDirectMessagePopup)
        {
            ImGui.OpenPopup(NewDirectMessagePopupId);
            requestOpenDirectMessagePopup = false;
        }

        if (requestOpenHiddenChannelsPopup)
        {
            ImGui.OpenPopup(HiddenChannelsPopupId);
            requestOpenHiddenChannelsPopup = false;
        }

        if (requestOpenRecentDirectMessagesPopup)
        {
            ImGui.OpenPopup(RecentDirectMessagesPopupId);
            requestOpenRecentDirectMessagesPopup = false;
        }
    }

    private void QueueOpenDirectMessagePopup(string? initialTarget = null)
    {
        if (!isMasterWindow)
            return;

        ClearTransientUiStatus();
        suppressSimpleComposerAutoFocusThisFrame = true;
        pendingDirectMessageTarget = initialTarget ?? string.Empty;
        directMessagePopupError = string.Empty;
        requestDirectMessageTargetFocus = true;
        requestOpenDirectMessagePopup = true;
    }

    private void DrawConversationToolbarButtons()
    {
        if (!isMasterWindow)
            return;

        var totalWidth = GetConversationToolbarWidth();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, ImGui.GetContentRegionAvail().X - totalWidth));

        var buttonSize=IsUltraCompactMode()?new Vector2(DhogGptPresentation.UltraTabHeight)*MaterialTheme.Metrics.Scale:Vector2.Zero;
        if (UiGui.Button("H",buttonSize))
        {
            ClearTransientUiStatus();
            suppressSimpleComposerAutoFocusThisFrame = true;
            requestOpenHiddenChannelsPopup = true;
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Show hidden channel tabs.");

        ImGui.SameLine();
        if (UiGui.Button("R",buttonSize))
        {
            ClearTransientUiStatus();
            suppressSimpleComposerAutoFocusThisFrame = true;
            requestOpenRecentDirectMessagesPopup = true;
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Reopen recent DM tabs.");

        ImGui.SameLine();
        if (UiGui.Button("+",buttonSize))
            QueueOpenDirectMessagePopup();
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Open a new DM.");
    }

    private void DrawHiddenChannelsPopup()
    {
        if (!isMasterWindow)
            return;

        if (!ImGui.BeginPopup(HiddenChannelsPopupId))
            return;

        var hiddenConversations = GetAllGeneralConversations()
            .Where(conversation => IsGeneralConversationHidden(conversation.Key))
            .Where(conversation => ShouldWindowDisplayConversation(conversation.Key))
            .ToList();

        if (hiddenConversations.Count == 0)
        {
            UiGui.TextDisabled("No channels are hidden.");
            ImGui.EndPopup();
            return;
        }

        foreach (var conversation in hiddenConversations)
        {
            var displayLabel = GetConversationDisplayLabel(conversation);
            if (!UiGui.Selectable(displayLabel, false))
                continue;

            ClearTransientUiStatus();
            SetGeneralConversationHidden(conversation.Key, false);
            activeConversationKey = conversation.Key;
            activeConversationLabel = displayLabel;
            forceActiveConversationSelection = true;
            requestWindowFocus = true;
            requestSimpleComposerFocus = true;
            TryApplyConversationKeyToOutgoingChannel(conversation.Key);
            plugin.Configuration.Save();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawRecentDirectMessagesPopup(IReadOnlyList<ConversationTabState> directMessageConversations)
    {
        if (!isMasterWindow)
            return;

        if (!ImGui.BeginPopup(RecentDirectMessagesPopupId))
            return;

        ImGui.SetNextItemWidth(280f);
        UiGui.InputTextWithHint("##RecentDmSearch", "Search recent DMs", ref recentDirectMessageSearch, 128);
        recentDirectMessageSearchInputRect = TrackedInputRect.CaptureCurrentItem();
        recentDirectMessageSearchFocusedLastFrame = ImGui.IsItemFocused();
        ImGui.Separator();

        var filteredConversations = directMessageConversations
            .Where(conversation => conversation.Messages.Count > 0)
            .Where(conversation => string.IsNullOrWhiteSpace(recentDirectMessageSearch) ||
                                   conversation.Label.Contains(recentDirectMessageSearch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(conversation => conversation.LastMessageUtc)
            .ToList();

        if (filteredConversations.Count == 0)
        {
            UiGui.TextDisabled("No recent DMs matched.");
            ImGui.EndPopup();
            return;
        }

        foreach (var conversation in filteredConversations)
        {
            var isPinned = IsPinnedDirectMessageConversation(conversation.Key);
            var label = isPinned ? $"{GetConversationDisplayLabel(conversation)} [P]" : GetConversationDisplayLabel(conversation);
            if (MaterialText.Selectable(label, false))
            {
                ClearTransientUiStatus();
                ReopenDirectMessageConversation(conversation);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (UiGui.SmallButton($"{(isPinned ? "Unpin" : "Pin")}##{conversation.Key}"))
            {
                ClearTransientUiStatus();
                SetPinnedDirectMessageConversation(conversation.Key, conversation.Label, !isPinned);
            }
        }

        ImGui.EndPopup();
    }

    private void ReopenDirectMessageConversation(ConversationTabState conversation)
    {
        if (ChatChannelMapper.TryNormalizeDirectMessageIdentity(conversation.Label, out var normalizedIdentity, out _))
        {
            OpenDirectMessageConversation(normalizedIdentity);
            return;
        }

        pendingDirectMessageTabs[conversation.Key] = conversation.Label;
        closedConversationCutoffs.Remove(conversation.Key);
        activeConversationKey = conversation.Key;
        activeConversationLabel = conversation.Label;
        forceActiveConversationSelection = true;
        requestWindowFocus = true;
        requestSimpleComposerFocus = true;
        if (isMasterWindow)
            ApplySavedPositionForCurrentCharacter();

        IsOpen = true;
    }

    private void DrawDirectMessageCreationPopup()
    {
        if (!UiGui.BeginPopupModal(NewDirectMessagePopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        UiGui.TextUnformatted("Open a DM by entering First Last@World.");
        if (requestDirectMessageTargetFocus)
        {
            ImGui.SetKeyboardFocusHere();
            requestDirectMessageTargetFocus = false;
        }

        var submit = UiGui.InputTextWithHint(
            "##DhogGPTNewDmTarget",
            "First Last@World",
            ref pendingDirectMessageTarget,
            128,
            ImGuiInputTextFlags.EnterReturnsTrue);
        newDirectMessageTargetInputRect = TrackedInputRect.CaptureCurrentItem();
        newDirectMessageTargetFocusedLastFrame = ImGui.IsItemFocused();

        if (!string.IsNullOrWhiteSpace(directMessagePopupError))
            UiGui.TextColored(new Vector4(1.0f, 0.55f, 0.55f, 1.0f), UiText.Status(directMessagePopupError));

        if (submit || UiGui.Button("Open"))
        {
            if (TryConfirmDirectMessagePopup())
                ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (UiGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            directMessagePopupError = string.Empty;
            pendingDirectMessageTarget = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private bool TryConfirmDirectMessagePopup()
    {
        if (!ChatChannelMapper.TryNormalizeDirectMessageIdentity(pendingDirectMessageTarget, out var normalizedIdentity, out var error))
        {
            directMessagePopupError = error;
            return false;
        }

        directMessagePopupError = string.Empty;
        ClearTransientUiStatus();
        OpenDirectMessageConversation(normalizedIdentity);
        pendingDirectMessageTarget = string.Empty;
        return true;
    }

    private void DrawDirectMessageTabContextMenu(ConversationTabState conversation, bool isPinnedDirectMessage)
    {
        if (!ImGui.BeginPopupContextItem($"DhogGPTDirectMessageContext##{conversation.Key}"))
            return;

        var canSpawnDetachedWindow = plugin.CanSpawnDetachedConversationWindow(conversation.Key);
        if (UiGui.Selectable(
                canSpawnDetachedWindow ? "Spawn new window" : "Already detached",
                false,
                canSpawnDetachedWindow ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
        {
            ClearTransientUiStatus();
            plugin.TrySpawnDetachedConversationWindow(conversation.Key, conversation.Label);
        }

        ImGui.Separator();

        if (conversation.Messages.Count > 0)
        {
            if (UiGui.Selectable(isPinnedDirectMessage ? "Unpin conversation" : "Pin conversation"))
            {
                ClearTransientUiStatus();
                SetPinnedDirectMessageConversation(conversation.Key, conversation.Label, !isPinnedDirectMessage);
            }
        }
        else
        {
            ImGui.BeginDisabled();
            UiGui.Selectable("Pin conversation");
            ImGui.EndDisabled();
        }

        if (UiGui.Selectable("Copy DM target"))
            ImGui.SetClipboardText(conversation.Label);

        if (UiGui.Selectable(isPinnedDirectMessage ? "Close pinned DM (hold Ctrl + click x)" : "Close DM"))
        {
            ClearTransientUiStatus();
            CloseConversation(conversation, isPinnedDirectMessage);
        }

        ImGui.EndPopup();
    }

    private void DrawDirectMessageTabPin(ConversationTabState conversation, bool isPinnedDirectMessage)
    {
        var tabMin = ImGui.GetItemRectMin();
        var tabMax = ImGui.GetItemRectMax();
        if (tabMax.X <= tabMin.X || tabMax.Y <= tabMin.Y)
            return;

        var pinMin = new Vector2(tabMin.X + 7f, tabMin.Y + 3f);
        var pinMax = new Vector2(pinMin.X + 14f, tabMax.Y - 3f);
        var pinColor = isPinnedDirectMessage
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.92f, 0.42f, 1.0f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.66f, 0.72f, 1.0f));

        MaterialText.AddText(ImGui.GetWindowDrawList(),new Vector2(pinMin.X + 1f, pinMin.Y - 1f), pinColor, "!");

        if (!ImGui.IsMouseHoveringRect(pinMin, pinMax))
            return;

        UiGui.SetTooltip(isPinnedDirectMessage ? "Pinned DM tab. Click to unpin." : "Click to pin this DM tab.");
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            ClearTransientUiStatus();
            SetPinnedDirectMessageConversation(conversation.Key, conversation.Label, !isPinnedDirectMessage);
        }
    }

    private static bool DrawOutgoingChannelSelectable(string label, bool isSelected, Action onSelected)
    {
        if (!UiGui.Selectable(label, isSelected))
        {
            if (isSelected)
                ImGui.SetItemDefaultFocus();
            return false;
        }

        onSelected();
        return true;
    }

    private bool DrawOutgoingConversationSelectable(ConversationTabState conversation)
    {
        var isSelected = conversation.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase);
        return DrawOutgoingChannelSelectable(GetConversationDisplayLabel(conversation), isSelected, () =>
        {
            ClearTransientUiStatus();
            TryApplyConversationKeyToOutgoingChannel(conversation.Key);
        });
    }

    private string GetOutgoingConversationDisplayLabel()
    {
        if (!TryGetComposerConversation(out var conversationKey, out var conversationLabel, out _))
            return "New DM";

        return ShellChannelDisplayService.GetDisplayLabel(plugin.Configuration, conversationKey, conversationLabel);
    }

    private void DrawGeneralConversationContextMenu(ConversationTabState conversation)
    {
        if (!ImGui.BeginPopupContextItem($"DhogGPTGeneralConversationContext##{conversation.Key}"))
            return;

        var isCombinedConversation = TryParseCombinedConversationKey(conversation.Key, out _);
        if (!isCombinedConversation && ShellChannelDisplayService.TryGetDescriptor(conversation.Key, out _))
        {
            var useTechnical = ShellChannelDisplayService.UsesTechnicalLabel(plugin.Configuration, conversation.Key);
            if (UiGui.Selectable("Use in-game name", !useTechnical))
            {
                if (ShellChannelDisplayService.SetUseTechnicalLabel(plugin.Configuration, conversation.Key, false))
                    plugin.Configuration.Save();
            }

            if (UiGui.Selectable("Use technical name", useTechnical))
            {
                if (ShellChannelDisplayService.SetUseTechnicalLabel(plugin.Configuration, conversation.Key, true))
                    plugin.Configuration.Save();
            }

            ImGui.Separator();
        }

        var canSpawnDetachedWindow = !isCombinedConversation && plugin.CanSpawnDetachedConversationWindow(conversation.Key);
        if (UiGui.Selectable(
                canSpawnDetachedWindow ? "Spawn new window" : "Already detached",
                false,
                canSpawnDetachedWindow ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
        {
            ClearTransientUiStatus();
            plugin.TrySpawnDetachedConversationWindow(conversation.Key, conversation.Label);
        }

        ImGui.Separator();

        if (isMasterWindow && UiGui.BeginMenu("Combine with"))
        {
            var combineTargets = GetAvailableCombineTargets(conversation.Key);
            if (combineTargets.Count == 0)
            {
                ImGui.BeginDisabled();
                UiGui.MenuItem("No eligible channels");
                ImGui.EndDisabled();
            }
            else
            {
                foreach (var targetConversation in combineTargets)
                {
                    if (!UiGui.MenuItem(GetConversationDisplayLabel(targetConversation)))
                        continue;

                    ClearTransientUiStatus();
                    CombineGeneralConversation(conversation.Key, targetConversation.Key);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndMenu();
        }

        if (isCombinedConversation)
        {
            if (UiGui.Selectable("Release combined channels"))
            {
                ClearTransientUiStatus();
                ReleaseCombinedConversation(conversation.Key);
                ImGui.CloseCurrentPopup();
            }

            UiGui.TextDisabled("Click x to release the combined tab back into separate channels.");
        }
        else
        {
            UiGui.TextDisabled("Hold Ctrl and click x to hide this channel tab.");
        }

        ImGui.EndPopup();
    }

    private bool IsPinnedDirectMessageConversation(string conversationKey)
        => plugin.Configuration.PinnedDirectMessageTabs.Any(pinned => string.Equals(pinned, conversationKey, StringComparison.OrdinalIgnoreCase));

    private void SetPinnedDirectMessageConversation(string conversationKey, string label, bool shouldPin)
    {
        ClearTransientUiStatus();
        var pinnedTabs = plugin.Configuration.PinnedDirectMessageTabs;
        pinnedTabs.RemoveAll(existing => string.Equals(existing, conversationKey, StringComparison.OrdinalIgnoreCase));
        if (shouldPin)
            pinnedTabs.Add(conversationKey);

        if (shouldPin)
            pendingDirectMessageTabs[conversationKey] = label;

        plugin.Configuration.Save();
    }

    private void OnTranslationCompleted(TranslationResult result)
    {
        if (!isMasterWindow)
            return;

        if (!plugin.Configuration.OpenMainWindowOnIncomingDirectMessage ||
            !result.Success ||
            !result.Request.IsInbound ||
            !string.Equals(result.Request.ChannelLabel, "DM", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.Request.ConversationLabel))
        {
            return;
        }

        _ = Plugin.Framework.RunOnFrameworkThread(() => OpenDirectMessageConversation(result.Request.ConversationLabel));
    }

    private void OnIncomingDirectMessageObserved(string identity)
    {
        if (!isMasterWindow)
            return;

        if (!plugin.Configuration.OpenMainWindowOnIncomingDirectMessage)
            return;

        _ = Plugin.Framework.RunOnFrameworkThread(() => OpenDirectMessageConversation(identity));
    }

    internal void AttachDetachedConversation(string conversationKey, string conversationLabel)
    {
        if (IsDirectMessageConversation(conversationKey) && !string.IsNullOrWhiteSpace(conversationLabel))
            pendingDirectMessageTabs[conversationKey] = conversationLabel;

        outgoingDraft = string.Empty;
        closedConversationCutoffs.Remove(conversationKey);
        activeConversationKey = conversationKey;
        activeConversationLabel = string.IsNullOrWhiteSpace(conversationLabel)
            ? GetDefaultConversationLabel(conversationKey)
            : conversationLabel;
        forceActiveConversationSelection = true;
        requestWindowFocus = true;
        requestSimpleComposerFocus = true;
        IsOpen = true;
    }

    internal void ReturnDetachedConversationToPrimaryLists(string conversationKey)
    {
        if (!isMasterWindow)
            return;

        if (TryResolveConversationTabState(conversationKey, out var conversation))
        {
            if (IsDirectMessageConversation(conversationKey))
            {
                pendingDirectMessageTabs.Remove(conversation.Key);
                closedConversationCutoffs[conversation.Key] = conversation.LastMessageUtc;
            }
            else
            {
                SetGeneralConversationHidden(conversation.Key, true);
            }
        }
        else if (IsDirectMessageConversation(conversationKey))
        {
            pendingDirectMessageTabs.Remove(conversationKey);
            closedConversationCutoffs[conversationKey] = DateTimeOffset.UtcNow;
        }
        else
        {
            SetGeneralConversationHidden(conversationKey, true);
        }

        if (conversationKey.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase))
        {
            activeConversationKey = string.Empty;
            activeConversationLabel = string.Empty;
            forceActiveConversationSelection = true;
        }
    }

    private bool ShouldWindowDisplayConversation(string conversationKey)
        => plugin.ShouldWindowDisplayConversation(conversationWindowId, isMasterWindow, conversationKey);

    private (string Key, string Label) GetPreferredConversationForInsertion()
    {
        if (!string.IsNullOrWhiteSpace(activeConversationKey))
            return (activeConversationKey, string.IsNullOrWhiteSpace(activeConversationLabel) ? GetDefaultConversationLabel(activeConversationKey) : activeConversationLabel);

        return ChatChannelMapper.GetOutgoingConversation(plugin.Configuration);
    }

    private static bool ShouldShowTranslatedLine(TranslationHistoryItem message)
    {
        if (!message.Success || string.IsNullOrWhiteSpace(message.TranslatedText))
            return false;

        return !string.Equals(Normalize(message.OriginalText), Normalize(message.TranslatedText), StringComparison.OrdinalIgnoreCase);
    }

    private bool TryDrawOriginalMessagePayload(TranslationHistoryItem message, int messageIndex)
    {
        if (!message.TryGetOriginalSeStringBytes(out var originalSeStringBytes))
            return false;

        var drawParams = new SeStringDrawParams();
        var drawResult = ImGuiHelpers.SeStringWrapped(
            originalSeStringBytes,
            in drawParams,
            new ImGuiId($"DhogGPTConversationPayload##{message.TimestampUtc.UtcTicks}:{messageIndex}:{message.ConversationKey}"),
            ImGuiButtonFlags.MouseButtonLeft);

        HandleConversationPayloadInteraction(drawResult);
        return true;
    }

    private void HandleConversationPayloadInteraction(in SeStringDrawResult drawResult)
    {
        if (drawResult.InteractedPayload is ItemPayload itemPayload)
        {
            var hoveredItemId = itemPayload.IsHQ
                ? itemPayload.ItemId + 1_000_000u
                : itemPayload.ItemId;
            Plugin.GameGui.HoveredItem = hoveredItemId;
            hoveredConversationItemThisFrame = true;
        }

        if (!drawResult.Clicked)
            return;

        if (drawResult.InteractedPayload is MapLinkPayload mapLinkPayload)
            Plugin.GameGui.OpenMapWithMapLink(mapLinkPayload);
    }

    private void UpdateHoveredConversationItemState()
    {
        if (!hoveredConversationItemThisFrame && hoveredConversationItemLastFrame)
            Plugin.GameGui.HoveredItem = 0;

        hoveredConversationItemLastFrame = hoveredConversationItemThisFrame;
    }

    public void ApplySavedPositionForCurrentCharacter()
    {
        if (!isMasterWindow)
            return;

        if (plugin.TryGetSavedWindowPosition(false, out var position))
        {
            if (plugin.Configuration.KeepWindowsOnCurrentGameScreen)
            {
                QueueViewportPlacement(position.ToVector2(), "saved main-window restore");
                return;
            }

            Position = position.ToVector2();
            PositionCondition = ImGuiCond.Always;
            pendingSavedPositionApply = true;
            return;
        }

        if (plugin.Configuration.KeepWindowsOnCurrentGameScreen)
        {
            QueueViewportPlacement(new Vector2(1f, 1f), "fallback main-window restore", forceSizeRepair: true);
            return;
        }

        Position = new Vector2(1f, 1f);
        PositionCondition = ImGuiCond.Always;
        pendingSavedPositionApply = true;
    }

    public void RequestSimpleComposerFocus()
        => requestSimpleComposerFocus = true;

    public void QueueRandomVisibleJump()
        => QueueViewportPlacement(null, "command /dgpt j", randomize: true, forceSizeRepair: true);

    public void OpenComposerFromHotkey(bool seedSlash)
    {
        if (!IsOpen)
            ApplySavedPositionForCurrentCharacter();

        if (seedSlash)
        {
            OpenSlashCommandConversation();
            var draft = outgoingDraft;
            if (!draft.StartsWith("/", StringComparison.Ordinal))
            {
                outgoingDraft = "/";
                if (isMasterWindow)
                    plugin.Configuration.OutgoingDraft = "/";
            }
        }

        IsOpen = true;
        requestWindowFocus = true;
        requestSimpleComposerFocus = true;
    }

    private bool IsUltraCompactMode()
        => plugin.Configuration.UseSimpleChatMode &&
           plugin.Configuration.CompactSimpleChatMode;

    private void OnLockTitleBarButtonClick(ImGuiMouseButton mouseButton)
    {
        if (mouseButton != ImGuiMouseButton.Left)
            return;

        plugin.Configuration.LockMainWindowPosition = !plugin.Configuration.LockMainWindowPosition;
        plugin.Configuration.Save();
        lockTitleBarButton.Icon = plugin.Configuration.LockMainWindowPosition ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }

    private void RecordSlashCommandEcho(string command)
    {
        var sender = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "You";
        chatLogService.AddTransientEntry(new TranslationHistoryItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            IsInbound = false,
            Success = true,
            ChannelLabel = "Safe",
            Sender = sender,
            ConversationKey = "channel:ECHO",
            ConversationLabel = "Safe",
            OriginalText = command,
            ProviderName = "SlashCommand",
        });

        Plugin.ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = $"[DhogGPT] Slash command sent: {command}",
        });
    }

    private void OpenSlashCommandConversation()
    {
        ClearTransientUiStatus();
        var changed = plugin.Configuration.HiddenGeneralConversationKeys.RemoveAll(hidden =>
            string.Equals(hidden, "channel:ECHO", StringComparison.OrdinalIgnoreCase)) > 0;

        activeConversationKey = "channel:ECHO";
        activeConversationLabel = "Safe";
        forceActiveConversationSelection = true;
        changed |= TryApplyConversationKeyToOutgoingChannel("channel:ECHO");

        if (changed)
            plugin.Configuration.Save();
    }

    private void ResetTrackedInputRects()
    {
        simpleComposerInputRect = default;
        recentDirectMessageSearchInputRect = default;
        newDirectMessageTargetInputRect = default;
        simpleComposerFocusedLastFrame = false;
        recentDirectMessageSearchFocusedLastFrame = false;
        newDirectMessageTargetFocusedLastFrame = false;
    }

    public bool ShouldAllowUltraCompactFocusHotkeyCapture()
        => IsOpen &&
           !simpleComposerFocusedLastFrame &&
           !recentDirectMessageSearchFocusedLastFrame &&
           !newDirectMessageTargetFocusedLastFrame;

    public string DescribeUltraCompactFocusHotkeyState()
        => $"windowOpen={IsOpen}, windowHovered={windowHoveredLastFrame}, windowFocused={windowFocusedLastFrame}, composerFocused={simpleComposerFocusedLastFrame}, recentDmSearchFocused={recentDirectMessageSearchFocusedLastFrame}, newDmTargetFocused={newDirectMessageTargetFocusedLastFrame}, popupOpen={anyPopupOpenLastFrame}";

    private void DrawConversationScrollIndicators(bool canScrollUp, bool canScrollDown)
    {
        if (!canScrollUp && !canScrollDown)
            return;

        if (plugin.Configuration.ScrollIndicatorStyle == 0)
        {
            var wedgeDrawList = ImGui.GetWindowDrawList();
            var wedgeWindowPos = ImGui.GetWindowPos();
            var wedgeWindowSize = ImGui.GetWindowSize();

            if (canScrollUp)
                DrawConversationScrollWedge(wedgeDrawList, wedgeWindowPos, wedgeWindowSize, top: true);

            if (canScrollDown)
                DrawConversationScrollWedge(wedgeDrawList, wedgeWindowPos, wedgeWindowSize, top: false);

            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var availableWidth = Math.Max(120f, windowSize.X - 18f);

        if (canScrollUp)
            DrawConversationScrollIndicator(drawList, windowPos, windowSize, BuildScrollIndicator('↑', availableWidth), top: true);

        if (canScrollDown)
            DrawConversationScrollIndicator(drawList, windowPos, windowSize, BuildScrollIndicator('↓', availableWidth), top: false);
    }

    private static void DrawConversationScrollWedge(ImDrawListPtr drawList, Vector2 windowPos, Vector2 windowSize, bool top)
    {
        var wedgeHeight = Math.Max(ImGui.GetTextLineHeight(), 14f);
        var wedgeWidth = Math.Max(72f, Math.Min(windowSize.X / 3f, windowSize.X - 24f));
        var centerX = windowPos.X + (windowSize.X * 0.5f);
        var originY = top
            ? windowPos.Y + 4f
            : windowPos.Y + windowSize.Y - wedgeHeight - 4f;

        var left = top
            ? new Vector2(centerX - (wedgeWidth * 0.5f), originY + wedgeHeight)
            : new Vector2(centerX - (wedgeWidth * 0.5f), originY);
        var right = top
            ? new Vector2(centerX + (wedgeWidth * 0.5f), originY + wedgeHeight)
            : new Vector2(centerX + (wedgeWidth * 0.5f), originY);
        var apex = top
            ? new Vector2(centerX, originY)
            : new Vector2(centerX, originY + wedgeHeight);

        drawList.AddTriangleFilled(
            left,
            apex,
            right,
            ImGui.GetColorU32(new Vector4(0.86f, 0.86f, 0.86f, 0.70f)));
        drawList.AddTriangle(
            left,
            apex,
            right,
            ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.08f, 0.75f)),
            1.5f);
    }

    private static void DrawConversationScrollIndicator(ImDrawListPtr drawList, Vector2 windowPos, Vector2 windowSize, string indicator, bool top)
    {
        var textSize = MaterialText.Measure(indicator);
        var position = new Vector2(
            windowPos.X + Math.Max(8f, (windowSize.X - textSize.X) * 0.5f),
            top
                ? windowPos.Y + 2f
                : windowPos.Y + windowSize.Y - textSize.Y - 2f);
        var padding = new Vector2(6f, 2f);
        drawList.AddRectFilled(
            position - padding,
            position + textSize + padding,
            ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.08f, 0.55f)),
            4f);
        MaterialText.AddText(drawList,position, ImGui.GetColorU32(new Vector4(0.78f, 0.78f, 0.78f, 0.95f)), indicator);
    }

    private static string BuildScrollIndicator(char direction, float width)
    {
        var segment = $"{direction} ";
        var segmentWidth = Math.Max(1f, MaterialText.Measure(segment).X);
        var repeatCount = Math.Max(8, (int)MathF.Ceiling(width / segmentWidth));
        var builder = new StringBuilder(repeatCount * segment.Length);
        for (var index = 0; index < repeatCount; index++)
        {
            if (index > 0)
                builder.Append(' ');

            builder.Append(direction);
        }

        return builder.ToString();
    }

    private float GetInactiveSimpleComposerOpacity()
        => Math.Clamp(plugin.Configuration.WindowOpacity, 0.20f, 1.0f);

    private void UpdateWindowOpacityState()
    {
        windowHoveredLastFrame = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        windowFocusedLastFrame = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
    }

    private void HandleConversationTabWheelNavigation(IReadOnlyList<ConversationTabState> conversations)
    {
        var io = ImGui.GetIO();
        if (Math.Abs(io.MouseWheel) <= float.Epsilon ||
            Math.Abs(io.MouseWheelH) > float.Epsilon ||
            conversations.Count < 2)
        {
            return;
        }

        var tabBarMin = ImGui.GetCursorScreenPos();
        var tabBarSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight());
        if (tabBarSize.X <= 0f ||
            tabBarSize.Y <= 0f ||
            !new TrackedInputRect(tabBarMin, tabBarMin + tabBarSize).Contains(io.MousePos) ||
            !WillConversationTabBarOverflow(conversations, tabBarSize.X))
        {
            return;
        }

        SelectConversationRelativeToWheel(conversations, io.MouseWheel);
        io.MouseWheel = 0f;
    }

    private void SelectConversationRelativeToWheel(IReadOnlyList<ConversationTabState> conversations, float wheelDelta)
    {
        if (conversations.Count == 0)
            return;

        var currentIndex = conversations
            .Select((conversation, index) => new { conversation.Key, Index = index })
            .FirstOrDefault(item => item.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase))
            ?.Index ?? 0;
        var direction = wheelDelta < 0f ? 1 : -1;
        var targetIndex = Math.Clamp(currentIndex + direction, 0, conversations.Count - 1);
        if (targetIndex == currentIndex)
            return;

        activeConversationKey = conversations[targetIndex].Key;
        activeConversationLabel = conversations[targetIndex].Label;
        forceActiveConversationSelection = true;
    }

    private float EstimateConversationTabBarWidth(IReadOnlyList<ConversationTabState> conversations)
    {
        var style = ImGui.GetStyle();
        var closeButtonWidth = ImGui.GetFontSize();
        var totalWidth = 0f;

        foreach (var conversation in conversations)
        {
            var label = GetConversationDisplayLabel(conversation);
            if (IsDirectMessageConversation(conversation.Key))
                label = $"   {label}";

            totalWidth += MaterialText.Measure(label).X;
            totalWidth += (style.FramePadding.X * 2f) + closeButtonWidth + style.ItemInnerSpacing.X + style.ItemSpacing.X;
        }

        return totalWidth;
    }

    private bool WillConversationTabBarOverflow(IReadOnlyList<ConversationTabState> conversations, float availableWidth)
        => availableWidth > 0f && EstimateConversationTabBarWidth(conversations) > availableWidth;

    private float GetConversationToolbarWidth()
    {
        if (!isMasterWindow)
            return 0f;

        var style = ImGui.GetStyle();
        var labels = new[] { "H", "R", "+" };
        var totalWidth = 0f;

        foreach (var label in labels)
            totalWidth += Math.Max(IsUltraCompactMode()?DhogGptPresentation.UltraTabHeight*MaterialTheme.Metrics.Scale:0,
                MaterialText.Measure(label).X + (style.FramePadding.X * 2f));

        totalWidth += style.ItemSpacing.X * (labels.Length - 1);
        return totalWidth + Math.Max(6f, style.CellPadding.X * 2f);
    }

    private static void PushConversationTabStyleColors(
        Vector4 tab,
        Vector4 tabHovered,
        Vector4 tabActive,
        Vector4 tabUnfocused,
        Vector4 tabUnfocusedActive)
    {
        ImGui.PushStyleColor(ImGuiCol.Tab, tab);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, tabHovered);
        ImGui.PushStyleColor(ImGuiCol.TabActive, tabActive);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, tabUnfocused);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, tabUnfocusedActive);
    }

    private static Vector4 WithMinimumAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, Math.Clamp(alpha, 0f, 1f));

    private void HandleSimpleComposerAutoFocusFromClick()
    {
        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left) ||
            !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) ||
            suppressSimpleComposerAutoFocusThisFrame ||
            ImGui.IsAnyItemActive() ||
            ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
        {
            return;
        }

        var mousePosition = ImGui.GetIO().MousePos;
        if (simpleComposerInputRect.Contains(mousePosition) ||
            recentDirectMessageSearchInputRect.Contains(mousePosition) ||
            newDirectMessageTargetInputRect.Contains(mousePosition))
        {
            return;
        }

        requestSimpleComposerFocus = true;
    }

    private void QueueViewportPlacement(Vector2? requestedPosition, string reason, bool randomize = false, bool forceSizeRepair = false, Vector2? requestedSize = null)
    {
        pendingViewportPlacementPosition = requestedPosition;
        pendingViewportPlacementSize = requestedSize;
        pendingViewportPlacementReason = reason;
        pendingRandomViewportPlacement = randomize;
        pendingSizeRepair |= forceSizeRepair;
    }

    private Vector2 GetMinimumWindowSize()
        => IsUltraCompactMode()
            ? new Vector2(460f, 220f)
            : new Vector2(720f, 520f);

    private Vector2 GetPreferredWindowSize()
        => IsUltraCompactMode()
            ? new Vector2(620f, 320f)
            : new Vector2(960f, 700f);

    private Vector2 GetPlacementWindowSize(Vector2 workSize)
    {
        var minimumSize = GetMinimumWindowSize();
        var preferredSize =
            pendingViewportPlacementSize.HasValue
                ? pendingViewportPlacementSize.Value
                : !pendingSizeRepair &&
                  lastObservedWindowSize.X >= minimumSize.X - WindowRepairTolerance &&
                  lastObservedWindowSize.Y >= minimumSize.Y - WindowRepairTolerance
                ? lastObservedWindowSize
                : GetPreferredWindowSize();

        return WindowPlacementHelper.GetSafeWindowSize(minimumSize, preferredSize, workSize);
    }

    private void ApplyPendingViewportPlacement()
    {
        if (!pendingRandomViewportPlacement && !pendingViewportPlacementPosition.HasValue)
            return;

        var viewport = ImGui.GetMainViewport();
        var workPos = viewport.WorkPos;
        var workSize = viewport.WorkSize;
        var windowSize = GetPlacementWindowSize(workSize);
        var desiredPosition = pendingRandomViewportPlacement
            ? WindowPlacementHelper.BuildRandomVisiblePosition(windowSize, workPos, workSize)
            : pendingViewportPlacementPosition ?? WindowPlacementHelper.GetViewportTopLeft(workPos);
        var appliedPosition = WindowPlacementHelper.ClampToWorkArea(desiredPosition, windowSize, workPos, workSize);
        var clamped = Vector2.DistanceSquared(desiredPosition, appliedPosition) >= 0.25f;
        var reason = pendingViewportPlacementReason;
        var forcedSizeRepair = pendingSizeRepair;
        var randomPlacement = pendingRandomViewportPlacement;

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
            $"[DhogGPT] Main window placement applied: reason={reason}, " +
            $"desired={FormatVector2(desiredPosition)}, applied={FormatVector2(appliedPosition)}, " +
            $"windowSize={FormatVector2(windowSize)}, viewportWorkPos={FormatVector2(workPos)}, " +
            $"viewportWorkSize={FormatVector2(workSize)}, clamped={clamped}, " +
            $"random={randomPlacement}, forceSizeRepair={forcedSizeRepair}, ultraCompact={IsUltraCompactMode()}");

        pendingViewportPlacementPosition = null;
        pendingViewportPlacementSize = null;
        pendingViewportPlacementReason = string.Empty;
        pendingRandomViewportPlacement = false;
        pendingSizeRepair = false;
    }

    private bool TryQueueWindowRepair(Vector2 currentPosition, Vector2 currentSize)
    {
        if (!plugin.Configuration.KeepWindowsOnCurrentGameScreen)
            return false;

        if (pendingViewportPlacementPosition.HasValue || pendingRandomViewportPlacement)
            return true;

        var minimumSize = GetMinimumWindowSize();
        var tooSmall =
            currentSize.X < minimumSize.X - WindowRepairTolerance ||
            currentSize.Y < minimumSize.Y - WindowRepairTolerance;
        var viewport = ImGui.GetMainViewport();
        var safeSize = WindowPlacementHelper.GetSafeWindowSize(minimumSize, currentSize, viewport.WorkSize);
        var sizeNeedsRepair = tooSmall || Vector2.DistanceSquared(safeSize, currentSize) >= 0.25f;
        var offscreen = !WindowPlacementHelper.IsInsideWorkArea(currentPosition, currentSize, viewport.WorkPos, viewport.WorkSize);
        if (!sizeNeedsRepair && !offscreen)
            return false;

        var reason = tooSmall
            ? $"detected undersized main window at {FormatVector2(currentSize)}"
            : sizeNeedsRepair
                ? $"detected oversized main window at {FormatVector2(currentSize)}"
                : $"detected off-screen main window at {FormatVector2(currentPosition)}";
        QueueViewportPlacement(
            currentPosition,
            reason,
            forceSizeRepair: sizeNeedsRepair,
            requestedSize: sizeNeedsRepair ? currentSize : null);
        Plugin.Log.Warning(
            $"[DhogGPT] Main window repair queued: reason={reason}, " +
            $"currentPos={FormatVector2(currentPosition)}, currentSize={FormatVector2(currentSize)}, " +
            $"safeSize={FormatVector2(safeSize)}, " +
            $"viewportWorkPos={FormatVector2(viewport.WorkPos)}, viewportWorkSize={FormatVector2(viewport.WorkSize)}, " +
            $"ultraCompact={IsUltraCompactMode()}");
        return true;
    }

    private void LogWindowSnapshot(string reason, Vector2 currentPosition, Vector2 currentSize)
    {
        var viewport = ImGui.GetMainViewport();
        Plugin.Log.Information(
            $"[DhogGPT] Main window snapshot: reason={reason}, " +
            $"pos={FormatVector2(currentPosition)}, size={FormatVector2(currentSize)}, " +
            $"collapsed={ImGui.IsWindowCollapsed()}, viewportWorkPos={FormatVector2(viewport.WorkPos)}, " +
            $"viewportWorkSize={FormatVector2(viewport.WorkSize)}, ultraCompact={IsUltraCompactMode()}");
    }

    private static string FormatVector2(Vector2 value)
        => $"{value.X:F1},{value.Y:F1}";

    private void TrackWindowPosition()
    {
        var currentPosition = ImGui.GetWindowPos();
        var currentSize = windowMotion.GetLogicalSize();
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

        if (!isMasterWindow)
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
        plugin.SaveCurrentWindowPosition(false, currentPosition);
    }

    private (Vector4 Header, Vector4 Translation, Vector4 Error) GetMessagePalette(TranslationHistoryItem message)
    {
        var palette = GetBaseMessagePalette(message.IsInbound);

        if (!plugin.Configuration.UseRealChatColorParity)
            return palette;

        return TryGetRealChatHeaderColor(message.ChannelLabel, out var channelHeaderColor)
            ? (channelHeaderColor, palette.Item2, palette.Item3)
            : palette;
    }

    private (Vector4 Header, Vector4 Translation, Vector4 Error) GetBaseMessagePalette(bool isInbound)
    {
        return plugin.Configuration.CompactChatColorTheme switch
        {
            1 => isInbound
                ? (new Vector4(0.45f, 0.82f, 1.0f, 1.0f), new Vector4(0.95f, 0.98f, 1.0f, 1.0f), new Vector4(1.0f, 0.45f, 0.45f, 1.0f))
                : (new Vector4(0.42f, 1.0f, 0.62f, 1.0f), new Vector4(0.94f, 1.0f, 0.95f, 1.0f), new Vector4(1.0f, 0.45f, 0.45f, 1.0f)),
            2 => isInbound
                ? (new Vector4(0.42f, 0.70f, 1.0f, 1.0f), new Vector4(0.82f, 0.90f, 1.0f, 1.0f), new Vector4(0.95f, 0.38f, 0.38f, 1.0f))
                : (new Vector4(0.45f, 0.92f, 0.55f, 1.0f), new Vector4(0.85f, 1.0f, 0.88f, 1.0f), new Vector4(0.95f, 0.38f, 0.38f, 1.0f)),
            3 => isInbound
                ? (new Vector4(0.75f, 0.58f, 1.0f, 1.0f), new Vector4(0.94f, 0.88f, 1.0f, 1.0f), new Vector4(1.0f, 0.50f, 0.72f, 1.0f))
                : (new Vector4(0.38f, 1.0f, 0.92f, 1.0f), new Vector4(0.86f, 1.0f, 0.98f, 1.0f), new Vector4(1.0f, 0.50f, 0.72f, 1.0f)),
            4 => isInbound
                ? (plugin.Configuration.CompactChatCustomColors.GetInboundHeader(), plugin.Configuration.CompactChatCustomColors.GetInboundTranslation(), plugin.Configuration.CompactChatCustomColors.GetError())
                : (plugin.Configuration.CompactChatCustomColors.GetOutboundHeader(), plugin.Configuration.CompactChatCustomColors.GetOutboundTranslation(), plugin.Configuration.CompactChatCustomColors.GetError()),
            _ => isInbound
                ? (new Vector4(0.58f, 0.80f, 1.0f, 1.0f), new Vector4(0.84f, 0.92f, 1.0f, 1.0f), new Vector4(1.0f, 0.55f, 0.55f, 1.0f))
                : (new Vector4(0.66f, 0.96f, 0.72f, 1.0f), new Vector4(0.86f, 1.0f, 0.90f, 1.0f), new Vector4(1.0f, 0.55f, 0.55f, 1.0f)),
        };
    }

    private (Vector4 Tab, Vector4 TabHovered, Vector4 TabActive, Vector4 TabUnfocused, Vector4 TabUnfocusedActive, Vector4 TabText, Vector4 ActiveTabText) GetConversationTabPalette()
    {
        if (plugin.Configuration.CompactChatColorTheme == 4)
        {
            var customColors = plugin.Configuration.CompactChatCustomColors;
            return (
                customColors.GetTab(),
                customColors.GetTabHovered(),
                customColors.GetTabActive(),
                customColors.GetTabUnfocused(),
                customColors.GetTabUnfocusedActive(),
                customColors.GetTabText(),
                customColors.GetActiveTabText());
        }

        var themeColors=MaterialTheme.Current.Colors;
        return (themeColors.Surface,themeColors.SurfaceContainerHigh,themeColors.PrimaryContainer,
            themeColors.Surface,themeColors.SurfaceContainerHigh,themeColors.OnSurfaceVariant,themeColors.OnSurface);
    }


    private static Vector4 BlendColors(Vector4 left, Vector4 right, float amount)
    {
        var t = Math.Clamp(amount, 0f, 1f);
        return new Vector4(
            left.X + ((right.X - left.X) * t),
            left.Y + ((right.Y - left.Y) * t),
            left.Z + ((right.Z - left.Z) * t),
            left.W + ((right.W - left.W) * t));
    }

    private static Vector4 ScaleColorRgb(Vector4 color, float scale)
        => new(
            Math.Clamp(color.X * scale, 0f, 1f),
            Math.Clamp(color.Y * scale, 0f, 1f),
            Math.Clamp(color.Z * scale, 0f, 1f),
            color.W);

    private static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, Math.Clamp(alpha, 0f, 1f));

    private static Vector4 GetReadableTextColor(Vector4 background)
    {
        var luminance = (0.2126f * background.X) + (0.7152f * background.Y) + (0.0722f * background.Z);
        return luminance >= 0.58f
            ? new Vector4(0.05f, 0.05f, 0.05f, 1.0f)
            : new Vector4(0.95f, 0.97f, 1.0f, 1.0f);
    }

    private static bool TryGetRealChatHeaderColor(string channelLabel, out Vector4 color)
    {
        var normalized = Normalize(channelLabel).ToUpperInvariant();
        switch (normalized)
        {
            case "SAY":
                color = new Vector4(0.92f, 0.92f, 0.92f, 1.0f);
                return true;
            case "EMOTE":
                color = new Vector4(1.0f, 0.80f, 0.92f, 1.0f);
                return true;
            case "PARTY":
                color = new Vector4(0.47f, 0.72f, 1.0f, 1.0f);
                return true;
            case "ALLIANCE":
                color = new Vector4(1.0f, 0.78f, 0.38f, 1.0f);
                return true;
            case "FC":
                color = new Vector4(0.48f, 0.94f, 0.55f, 1.0f);
                return true;
            case "SHOUT":
                color = new Vector4(1.0f, 0.63f, 0.28f, 1.0f);
                return true;
            case "YELL":
                color = new Vector4(1.0f, 0.86f, 0.36f, 1.0f);
                return true;
            case "NN":
                color = new Vector4(0.60f, 1.0f, 0.72f, 1.0f);
                return true;
            case "PVP TEAM":
                color = new Vector4(1.0f, 0.52f, 0.72f, 1.0f);
                return true;
            case "DM":
                color = new Vector4(1.0f, 0.60f, 0.86f, 1.0f);
                return true;
            case "SAFE":
                color = new Vector4(0.74f, 0.82f, 0.92f, 1.0f);
                return true;
            case "ECHO":
            case "ECHO CHAT":
                color = new Vector4(0.80f, 0.82f, 0.88f, 1.0f);
                return true;
            case "PROGRESS":
                color = new Vector4(0.92f, 0.86f, 0.56f, 1.0f);
                return true;
            case "COMBAT":
                color = new Vector4(1.0f, 0.58f, 0.50f, 1.0f);
                return true;
        }

        if (normalized.StartsWith("LS", StringComparison.Ordinal))
        {
            color = new Vector4(0.88f, 0.64f, 1.0f, 1.0f);
            return true;
        }

        if (normalized.StartsWith("CWLS", StringComparison.Ordinal))
        {
            color = new Vector4(1.0f, 0.56f, 0.94f, 1.0f);
            return true;
        }

        color = default;
        return false;
    }

    private static bool IsSafeConversation(TranslationRequest request)
        => string.Equals(request.ConversationKey, "channel:ECHO", StringComparison.OrdinalIgnoreCase);

    private bool TryResolveConversationTabState(string conversationKey, out ConversationTabState conversation)
    {
        var entries = chatLogService.GetEntriesSnapshot();

        if (TryParseCombinedConversationKey(conversationKey, out var groupedConversationKeys))
        {
            var messages = entries
                .Where(entry => groupedConversationKeys.Any(groupedConversationKey =>
                    GetConversationGroupingKey(entry).Equals(groupedConversationKey, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(entry => entry.TimestampUtc)
                .ToList();
            conversation = new ConversationTabState(
                conversationKey,
                BuildCombinedConversationLabel(groupedConversationKeys),
                messages,
                messages.Count > 0 ? messages[^1].TimestampUtc : DateTimeOffset.MinValue);
            return true;
        }

        foreach (var generalConversation in GetAllGeneralConversations())
        {
            if (!generalConversation.Key.Equals(conversationKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var messages = entries
                .Where(entry => GetConversationGroupingKey(entry).Equals(conversationKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.TimestampUtc)
                .ToList();
            conversation = generalConversation with
            {
                Messages = messages,
                LastMessageUtc = messages.Count > 0 ? messages[^1].TimestampUtc : DateTimeOffset.MinValue,
            };
            return true;
        }

        var matchingMessages = entries
            .Where(entry => GetConversationGroupingKey(entry).Equals(conversationKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.TimestampUtc)
            .ToList();
        if (matchingMessages.Count > 0)
        {
            conversation = new ConversationTabState(
                conversationKey,
                ResolveConversationLabel(matchingMessages),
                matchingMessages,
                matchingMessages[^1].TimestampUtc);
            return true;
        }

        if (pendingDirectMessageTabs.TryGetValue(conversationKey, out var pendingLabel))
        {
            conversation = new ConversationTabState(
                conversationKey,
                pendingLabel,
                Array.Empty<TranslationHistoryItem>(),
                DateTimeOffset.MinValue);
            return true;
        }

        conversation = default!;
        return false;
    }

    private static bool IsDirectMessageConversation(string conversationKey)
        => conversationKey.StartsWith("dm:", StringComparison.OrdinalIgnoreCase);

    private sealed record ConversationTabState(
        string Key,
        string Label,
        IReadOnlyList<TranslationHistoryItem> Messages,
        DateTimeOffset LastMessageUtc);

    private readonly record struct ConversationScrollState(
        int MessageCount,
        long LastMessageTicks);

    private readonly record struct TrackedInputRect(
        Vector2 Min,
        Vector2 Max)
    {
        public bool Contains(Vector2 point)
            => Max.X > Min.X && Max.Y > Min.Y &&
               point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y;

        public static TrackedInputRect CaptureCurrentItem()
            => new(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }
}
