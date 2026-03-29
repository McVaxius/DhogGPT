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
    private const int AutoScrollSettleFrames = 2;
    private const int DefaultVisibleDirectMessageTabs = 3;
    private const float WindowRepairTolerance = 4f;
    private const string NewDirectMessagePopupId = "New DM###DhogGPTNewDmPopup";
    private const string RecentDirectMessagesPopupId = "Recent DMs###DhogGPTRecentDmPopup";
    private const string HiddenChannelsPopupId = "Hidden Channels###DhogGPTHiddenChannelsPopup";
    private const string MainWindowTitle = "###DhogGPTMain";

    private readonly Plugin plugin;
    private readonly LanguageRegistryService languageRegistry;
    private readonly TranslationCoordinator translationCoordinator;
    private readonly SessionHealthService sessionHealth;
    private readonly ChatLogService chatLogService;
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
    private bool useFocusedWindowOpacity = true;
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
        ChatLogService chatLogService)
        : base(MainWindowTitle)
    {
        this.plugin = plugin;
        this.languageRegistry = languageRegistry;
        this.translationCoordinator = translationCoordinator;
        this.sessionHealth = sessionHealth;
        this.chatLogService = chatLogService;
        this.translationCoordinator.TranslationCompleted += OnTranslationCompleted;
        this.plugin.ChatTranslationService.IncomingDirectMessageObserved += OnIncomingDirectMessageObserved;
        lockTitleBarButton = new TitleBarButton
        {
            Click = OnLockTitleBarButtonClick,
            Icon = plugin.Configuration.LockMainWindowPosition ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            IconOffset = new Vector2(2f, 1f),
            ShowTooltip = () => ImGui.SetTooltip(plugin.Configuration.LockMainWindowPosition
                ? "Unlock main window position"
                : "Lock main window position"),
        };
        TitleBarButtons.Add(lockTitleBarButton);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720f, 520f),
            MaximumSize = new Vector2(1400f, 1000f),
        };
        Size = new Vector2(960f, 700f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
        if (hoveredConversationItemLastFrame)
            Plugin.GameGui.HoveredItem = 0;

        translationCoordinator.TranslationCompleted -= OnTranslationCompleted;
        plugin.ChatTranslationService.IncomingDirectMessageObserved -= OnIncomingDirectMessageObserved;
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
                MinimumSize = new Vector2(460f, 220f),
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

        ImGui.SetNextWindowBgAlpha(GetActiveWindowOpacity());
    }

    public override void Draw()
    {
        ResetTrackedInputRects();
        hoveredConversationItemThisFrame = false;
        suppressSimpleComposerAutoFocusThisFrame = false;
        anyPopupOpenLastFrame = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup);

        if (!IsUltraCompactMode())
        {
            DrawHeader();
            ImGui.Separator();
        }

        if (IsUltraCompactMode())
        {
            DrawSimpleChatMode();
            HandleSimpleComposerAutoFocusFromClick();
            UpdateHoveredConversationItemState();
            UpdateWindowOpacityState();
            TrackWindowPosition();
            return;
        }

        DrawStatusPanel();
        ImGui.Separator();
        DrawComposer();
        UpdateHoveredConversationItemState();
        UpdateWindowOpacityState();
        TrackWindowPosition();
    }

    private void DrawHeader()
    {
        var configuration = plugin.Configuration;
        var koFiWidth = ImGui.CalcTextSize("Ko-fi").X + (ImGui.GetStyle().FramePadding.X * 2f);
        var discordWidth = ImGui.CalcTextSize("Discord").X + (ImGui.GetStyle().FramePadding.X * 2f);
        var supportWidth = koFiWidth + discordWidth + ImGui.GetStyle().ItemSpacing.X + 8f;

        if (ImGui.BeginTable("DhogGPTHeaderTop", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Support", ImGuiTableColumnFlags.WidthFixed, supportWidth);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextDisabled("Translation controls");

            ImGui.TableSetColumnIndex(1);
            if (ImGui.SmallButton("Ko-fi"))
                Process.Start(new ProcessStartInfo { FileName = Plugin.SupportUrl, UseShellExecute = true });
            ImGui.SameLine();
            if (ImGui.SmallButton("Discord"))
                Process.Start(new ProcessStartInfo { FileName = Plugin.DiscordUrl, UseShellExecute = true });

            ImGui.EndTable();
        }

        if (ImGui.SmallButton("Guide"))
            plugin.OpenFirstUseGuide();

        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        if (ImGui.SmallButton("Status to chat"))
            plugin.PrintStatus("DhogGPT is loaded and ready.");

        var enabled = configuration.PluginEnabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
            plugin.SetPluginEnabled(enabled);

        ImGui.SameLine();
        var dtrEnabled = configuration.DtrBarEnabled;
        if (ImGui.Checkbox("DTR Bar", ref dtrEnabled))
        {
            configuration.DtrBarEnabled = dtrEnabled;
            configuration.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Turn on ultra compact"))
            plugin.SetUltraCompactMode(true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Switch from regular mode to ultra compact mode.");

        ImGui.SameLine();
        if (ImGui.SmallButton(configuration.KrangleChatNames ? "Krangle Names: On" : "Krangle Names: Off"))
        {
            configuration.KrangleChatNames = !configuration.KrangleChatNames;
            configuration.Save();
        }

        ImGui.TextWrapped("Regular mode keeps the fuller translator surface available. Ultra compact gives you the tighter DhogGPT chat surface, and settings let you decide whether vanilla chat stays visible alongside it.");
    }

    private void DrawSimpleChatMode()
    {
        HandlePendingPopups();
        DrawSimpleChatStatusBanner();
        DrawSimpleLanguageBar();

        var composerHeight = ImGui.GetFrameHeightWithSpacing() + 10f;
        var conversationHeaderHeight = ImGui.GetFrameHeightWithSpacing() + 2f;
        var chatBodyHeight = Math.Max(72f, ImGui.GetContentRegionAvail().Y - composerHeight - conversationHeaderHeight);
        DrawTabbedConversationArea(chatBodyHeight);

        ImGui.Separator();
        DrawSimpleComposer();
        DrawDirectMessageCreationPopup();
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
            var compactSpacing = new Vector2(Math.Max(2f, originalSpacing.X * 0.40f), 0f);
            var compactFramePadding = new Vector2(Math.Max(2f, originalFramePadding.X * 0.60f), 0f);
            var labelWidth = Math.Max(ImGui.CalcTextSize("Them").X, ImGui.CalcTextSize("Me").X);
            var utilityWidth = IsUltraCompactMode()
                ? (ImGui.CalcTextSize("K").X + (compactFramePadding.X * 2f) + compactSpacing.X) +
                  (ImGui.CalcTextSize("S").X + (compactFramePadding.X * 2f) + compactSpacing.X)
                : 0f;
            var comboWidth = Math.Max(
                120f,
                (ImGui.GetContentRegionAvail().X - utilityWidth - labelWidth - compactSpacing.X - labelWidth - compactSpacing.X) * 0.5f);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, compactSpacing);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, compactFramePadding);
            if (IsUltraCompactMode())
            {
                var highlightKrangleButton = configuration.KrangleChatNames;
                if (highlightKrangleButton)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.56f, 0.32f, 0.95f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.66f, 0.38f, 0.95f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.24f, 0.48f, 0.28f, 0.95f));
                }
                if (ImGui.SmallButton("K##UltraCompactKrangle"))
                {
                    configuration.KrangleChatNames = !configuration.KrangleChatNames;
                    changed = true;
                }
                if (highlightKrangleButton)
                    ImGui.PopStyleColor(3);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(configuration.KrangleChatNames ? "Krangle names is on." : "Krangle names is off.");

                ImGui.SameLine();
                if (ImGui.SmallButton("S##UltraCompactSettings"))
                    plugin.ToggleConfigUi();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Open DhogGPT settings.");

                ImGui.SameLine();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Me");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Your typed language before DhogGPT translates it.");
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
                ImGui.SetTooltip("Set the language you are writing in.");

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Them");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The language DhogGPT should translate your outgoing text into.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(comboWidth);
            changed |= DrawLanguageCombo(
                "##SimpleThemLanguage",
                configuration.OutgoingTargetLanguage,
                value => configuration.OutgoingTargetLanguage = value,
                includeAuto: false);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Set the language your messages should be translated to.");
            ImGui.PopStyleVar(2);

            if (changed)
                configuration.Save();

            return;
        }

        if (ImGui.BeginTable("DhogGPTSimpleLanguageTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("Outgoing");
            changed |= DrawLanguageCombo("From##SimpleOutgoing", configuration.OutgoingSourceLanguage, value => configuration.OutgoingSourceLanguage = value, includeAuto: true);
            changed |= DrawLanguageCombo("To##SimpleOutgoing", configuration.OutgoingTargetLanguage, value => configuration.OutgoingTargetLanguage = value, includeAuto: false);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted("Incoming");
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
            else
                conversations.Add(pinnedConversation);
        }
        var configuredConversation = ChatChannelMapper.GetOutgoingConversation(plugin.Configuration);
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

        ApplyDefaultDirectMessageVisibility(currentLogIdentity, allDirectMessageConversations);

        conversations.AddRange(allDirectMessageConversations.Where(IsConversationVisible));

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
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style.CellPadding.X, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, Math.Min(style.FramePadding.Y, 2f)));
        if (!ImGui.BeginTable("DhogGPTConversationTabsLayout", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.PopStyleVar(3);
            DrawHiddenChannelsPopup();
            DrawRecentDirectMessagesPopup(allDirectMessageConversations);
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
        if (!ImGui.BeginTabBar("DhogGPTConversationTabs", ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.Reorderable))
        {
            ImGui.PopStyleColor(5);
            ImGui.TableSetColumnIndex(1);
            DrawConversationToolbarButtons();
            ImGui.EndTable();
            ImGui.PopStyleVar(3);
            DrawHiddenChannelsPopup();
            DrawRecentDirectMessagesPopup(allDirectMessageConversations);
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
            var tabVisible = ImGui.BeginTabItem(tabLabel, ref tabOpen, tabFlags);
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
                ImGui.SetTooltip("Hold Ctrl and click x to hide this channel tab.");

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
        DrawRecentDirectMessagesPopup(allDirectMessageConversations);
        if (selectedConversationApplied)
            forceActiveConversationSelection = false;

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

        if (!ImGui.BeginChild(
                "DhogGPTConversationBody",
                new Vector2(-1f, height),
                true,
                ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.EndChild();
            return;
        }

        pendingConversationBottomScrolls.TryGetValue(conversationKey, out var pendingBottomScrollFrames);
        var shouldAutoScrollThisFrame = pendingBottomScrollFrames > 0;

        if (messages.Count == 0)
        {
            ImGui.TextDisabled("No translated chat has been logged for this tab yet.");
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
            var (headerColor, translatedColor, errorColor) = GetMessagePalette(message.IsInbound);
            var timestamp = message.TimestampUtc.ToLocalTime().ToString("HH:mm");
            var displayName = GetDisplayName(message);
            var originalText = string.IsNullOrWhiteSpace(message.OriginalText) ? "(empty)" : message.OriginalText;
            var canShowTranslatedLine = ShouldShowTranslatedLine(message);
            var clipboardText = canShowTranslatedLine
                ? $"{timestamp} - {displayName} - {originalText}{Environment.NewLine}{message.TranslatedText}"
                : $"{timestamp} - {displayName} - {originalText}";

            ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
            ImGui.TextUnformatted($"{timestamp} - {displayName} - ");
            ImGui.PopStyleColor();
            ImGui.SameLine(0f, 0f);

            if (!TryDrawOriginalMessagePayload(message, messageIndex))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
                ImGui.TextWrapped(originalText);
                ImGui.PopStyleColor();
            }

            if (message.Success && canShowTranslatedLine)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, translatedColor);
                ImGui.TextWrapped(message.TranslatedText);
                ImGui.PopStyleColor();
            }
            else if (!string.IsNullOrWhiteSpace(message.Error))
            {
                ImGui.TextColored(errorColor, $"Translation failed: {message.Error}");
            }

            if (ImGui.BeginPopupContextItem($"DhogGPTMessageContext##{message.TimestampUtc.UtcTicks}{message.ConversationKey}"))
            {
                if (ImGui.Selectable("Copy original"))
                    ImGui.SetClipboardText(originalText);

                if (canShowTranslatedLine && ImGui.Selectable("Copy translation"))
                    ImGui.SetClipboardText(message.TranslatedText);

                if (ImGui.Selectable("Copy both"))
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

        var comboWidth = 150f;
        var framePadding = ImGui.GetStyle().FramePadding;
        var composerFramePadding = ultraCompactMode
            ? new Vector2(Math.Max(2f, framePadding.X * 0.55f), Math.Max(4f, framePadding.Y))
            : new Vector2(framePadding.X, Math.Max(framePadding.Y, 6f));
        var sendWidth = ultraCompactMode
            ? ImGui.CalcTextSize("Send").X + (composerFramePadding.X * 2f)
            : 70f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var entryWidth = Math.Max(120f, ImGui.GetContentRegionAvail().X - comboWidth - sendWidth - (spacing * 2f));
        var submitFromEnter = false;
        var composerFrameOpacity = simpleComposerEditSessionActive
            ? 1.0f
            : GetInactiveSimpleComposerOpacity();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, composerFramePadding);
        if (!ultraCompactMode)
        {
            ImGui.SetNextItemWidth(comboWidth);
            if (DrawOutgoingChannelCombo("##SimpleChannel"))
            {
                changed = true;
                SyncActiveConversationToOutgoingChannel();
            }

            ImGui.SameLine();
        }

        if (requestSimpleComposerFocus)
        {
            ImGui.SetKeyboardFocusHere();
            requestSimpleComposerFocus = false;
        }

        ImGui.SetNextItemWidth(ultraCompactMode ? -1f : entryWidth);
        var styleColors = ImGui.GetStyle().Colors;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, WithMinimumAlpha(styleColors[(int)ImGuiCol.FrameBg], composerFrameOpacity));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, WithMinimumAlpha(styleColors[(int)ImGuiCol.FrameBgHovered], composerFrameOpacity));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, WithMinimumAlpha(styleColors[(int)ImGuiCol.FrameBgActive], composerFrameOpacity));
        var draft = configuration.OutgoingDraft;
        submitFromEnter = ImGui.InputTextWithHint(
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
        if (draft != configuration.OutgoingDraft)
        {
            configuration.OutgoingDraft = draft;
            ClearTransientUiStatus();
            changed = true;
        }

        if (submitFromEnter || composerLostFocus)
            simpleComposerEditSessionActive = false;

        if (!ultraCompactMode)
        {
            ImGui.SameLine();
            if (ImGui.Button("Send##SimpleSend", new Vector2(sendWidth, 0f)))
                submitFromEnter = true;
        }
        ImGui.PopStyleVar();

        if (submitFromEnter)
        {
            if (string.IsNullOrWhiteSpace(configuration.OutgoingDraft))
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
            configuration.Save();
    }

    private void DrawStatusPanel()
    {
        var snapshot = sessionHealth.GetSnapshot();
        var configuration = plugin.Configuration;

        ImGui.Text($"Plugin: {(configuration.PluginEnabled ? "Enabled" : "Disabled")}");
        ImGui.Text($"DTR entry: {(configuration.DtrBarEnabled ? "Visible" : "Hidden")}");
        ImGui.Text($"Incoming translation: {(configuration.TranslateIncoming ? "On" : "Off")}");
        ImGui.Text($"Queued jobs: {snapshot.QueueDepth}");
        ImGui.Text($"Successes: {snapshot.SuccessCount}");
        ImGui.Text($"Failures: {snapshot.FailureCount}");

        if (!string.IsNullOrWhiteSpace(snapshot.LastProvider))
            ImGui.Text($"Last provider: {snapshot.LastProvider}");

        if (!string.IsNullOrWhiteSpace(snapshot.LastEndpoint))
            ImGui.TextWrapped($"Last endpoint: {snapshot.LastEndpoint}");

        if (snapshot.LastLatency > TimeSpan.Zero)
            ImGui.Text($"Last latency: {snapshot.LastLatency.TotalMilliseconds:F0} ms");

        if (snapshot.LastSuccessUtc.HasValue)
            ImGui.Text($"Last success (UTC): {snapshot.LastSuccessUtc:yyyy-MM-dd HH:mm:ss}");

        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
            ImGui.TextWrapped($"Last error: {snapshot.LastError}");
    }

    private void DrawComposer()
    {
        var configuration = plugin.Configuration;
        var changed = false;

        ImGui.TextUnformatted("Outgoing translation composer");

        changed |= DrawLanguageCombo("From", configuration.OutgoingSourceLanguage, value => configuration.OutgoingSourceLanguage = value, includeAuto: true);
        changed |= DrawLanguageCombo("To", configuration.OutgoingTargetLanguage, value => configuration.OutgoingTargetLanguage = value, includeAuto: false);

        if (DrawOutgoingChannelCombo("Channel"))
            changed = true;

        var outgoingDraft = configuration.OutgoingDraft;
        if (ImGui.InputTextMultiline("Message", ref outgoingDraft, 2000, new Vector2(-1f, 90f)))
        {
            configuration.OutgoingDraft = outgoingDraft;
            changed = true;
        }

        if (changed)
            configuration.Save();

        if (previewBusy)
            ImGui.BeginDisabled();

        if (ImGui.Button("Preview translation"))
            _ = PreviewAsync(sendAfterTranslate: false);

        ImGui.SameLine();
        if (ImGui.Button("Translate and send"))
            _ = PreviewAsync(sendAfterTranslate: true);

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            configuration.OutgoingDraft = string.Empty;
            configuration.Save();
            previewStatus = string.Empty;
            previewText = string.Empty;
            previewMetadata = string.Empty;
        }

        if (previewBusy)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(previewStatus))
            ImGui.TextWrapped(previewStatus);

        if (!string.IsNullOrWhiteSpace(previewMetadata))
            ImGui.TextWrapped(previewMetadata);

        if (!string.IsNullOrWhiteSpace(previewText))
            ImGui.InputTextMultiline("Translated preview", ref previewText, 4000, new Vector2(-1f, 110f), ImGuiInputTextFlags.ReadOnly);

        DrawDirectMessageCreationPopup();
    }

    private async Task PreviewAsync(bool sendAfterTranslate)
    {
        if (previewBusy)
            return;

        previewBusy = true;
        previewStatus = "Working...";
        previewText = string.Empty;
        previewMetadata = string.Empty;

        try
        {
            if (TryExtractRawSlashCommand(plugin.Configuration.OutgoingDraft, out _))
            {
                if (!sendAfterTranslate)
                {
                    await SetPreviewStateAsync(status: "Slash commands are sent directly and are not translated or logged.");
                    return;
                }

                await TryHandleRawSlashCommandAsync(simpleChatMode: false);
                return;
            }

            var request = BuildOutgoingRequest(recordInHistory: false);
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

            if (request.ChannelLabel.Equals("Echo", StringComparison.OrdinalIgnoreCase))
            {
                RecordSuccessfulSend(result);
                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    plugin.Configuration.OutgoingDraft = string.Empty;
                    plugin.Configuration.Save();
                });

                activeConversationKey = request.ConversationKey;
                activeConversationLabel = request.ConversationLabel;
                forceActiveConversationSelection = true;
                await SetPreviewStateAsync(status: "Saved to Echo.", text: result.TranslatedText);
                return;
            }

            if (!CommandHelper.TryBuildOutgoingCommand(plugin.Configuration, result.TranslatedText, out var command, out var error))
            {
                await SetPreviewStateAsync(status: error);
                return;
            }

            if (!TryValidateOutgoingDirectMessageSend(out var sendValidationError))
            {
                await SetPreviewStateAsync(status: sendValidationError);
                return;
            }

            var sent = await Plugin.Framework.RunOnFrameworkThread(() => CommandHelper.SendCommand(command));
            if (sent)
                RecordSuccessfulSend(result);

            await SetPreviewStateAsync(status: sent
                ? $"Sent translated message to {ChatChannelMapper.GetOutgoingLabel(plugin.Configuration)}."
                : "Translation succeeded, but sending the message failed.");
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
        simpleChatStatus = "Translating...";

        try
        {
            if (await TryHandleRawSlashCommandAsync(simpleChatMode: true))
                return;

            var request = BuildOutgoingRequest(recordInHistory: false);
            var result = await TranslateOutgoingRequestAsync(request);
            if (!result.Success)
            {
                await SetSimpleChatStatusAsync($"Translation failed: {result.Error}");
                return;
            }

            if (request.ChannelLabel.Equals("Echo", StringComparison.OrdinalIgnoreCase))
            {
                RecordSuccessfulSend(result);

                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    plugin.Configuration.OutgoingDraft = string.Empty;
                    plugin.Configuration.Save();
                });

                activeConversationKey = request.ConversationKey;
                activeConversationLabel = request.ConversationLabel;
                forceActiveConversationSelection = true;
                await SetSimpleChatStatusAsync(string.Empty);
                return;
            }

            if (!CommandHelper.TryBuildOutgoingCommand(plugin.Configuration, result.TranslatedText, out var command, out var error))
            {
                await SetSimpleChatStatusAsync(error);
                return;
            }

            if (!TryValidateOutgoingDirectMessageSend(out var sendValidationError))
            {
                await SetSimpleChatStatusAsync(sendValidationError);
                return;
            }

            var sent = await Plugin.Framework.RunOnFrameworkThread(() => CommandHelper.SendCommand(command));
            if (!sent)
            {
                await SetSimpleChatStatusAsync("Translation succeeded, but sending the message failed.");
                return;
            }

            RecordSuccessfulSend(result);

            await Plugin.Framework.RunOnFrameworkThread(() =>
            {
                plugin.Configuration.OutgoingDraft = string.Empty;
                plugin.Configuration.Save();
            });

        activeConversationKey = request.ConversationKey;
        activeConversationLabel = request.ConversationLabel;
        forceActiveConversationSelection = true;
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
        var configuration = plugin.Configuration;
        var conversation = ChatChannelMapper.GetOutgoingConversation(configuration);

        return new TranslationRequest
        {
            Text = configuration.OutgoingDraft,
            SourceLanguage = configuration.OutgoingSourceLanguage,
            TargetLanguage = configuration.OutgoingTargetLanguage,
            IsInbound = false,
            ChannelLabel = ChatChannelMapper.GetOutgoingLabel(configuration),
            Sender = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "You",
            ConversationKey = conversation.Key,
            ConversationLabel = conversation.Label,
            RecordInHistory = recordInHistory,
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
        if (!TryExtractRawSlashCommand(plugin.Configuration.OutgoingDraft, out var command))
            return false;

        var sent = await Plugin.Framework.RunOnFrameworkThread(() => CommandHelper.SendCommand(command));
        if (sent)
        {
            await Plugin.Framework.RunOnFrameworkThread(() =>
            {
                RecordSlashCommandEcho(command);
                plugin.Configuration.OutgoingDraft = string.Empty;
                plugin.Configuration.Save();
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
        ImGui.TextWrapped(simpleChatStatus);
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
        if (plugin.Configuration.SelectedOutgoingChannel != OutgoingChannel.Tell)
            return true;

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

    private bool DrawOutgoingChannelCombo(string label)
    {
        var changed = false;
        var selectedLabel = GetOutgoingConversationDisplayLabel();

        if (!ImGui.BeginCombo(label, selectedLabel))
            return false;

        suppressSimpleComposerAutoFocusThisFrame = true;

        foreach (var conversation in GetPinnedGeneralConversations())
            changed |= DrawOutgoingConversationSelectable(conversation);

        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.95f, 0.55f, 1.0f));
        if (ImGui.Selectable("New DM", false))
            QueueOpenDirectMessagePopup();
        ImGui.PopStyleColor();

        ImGui.EndCombo();
        return changed;
    }

    private bool DrawLanguageCombo(string label, string currentCode, Action<string> setter, bool includeAuto)
    {
        var changed = false;
        var options = includeAuto ? languageRegistry.GetSourceLanguages() : languageRegistry.GetTargetLanguages();
        var displayName = languageRegistry.GetName(currentCode);

        if (ImGui.BeginCombo(label, displayName))
        {
            suppressSimpleComposerAutoFocusThisFrame = true;
            foreach (var option in options)
            {
                var isSelected = option.Code.Equals(currentCode, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(option.Name, isSelected))
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
        if (!IsDirectMessageConversation(conversation.Key))
            return ShellChannelDisplayService.GetDisplayLabel(plugin.Configuration, conversation.Key, conversation.Label);

        if (!plugin.Configuration.KrangleChatNames)
            return conversation.Label;

        if (!conversation.Key.StartsWith("dm:", StringComparison.OrdinalIgnoreCase))
            return conversation.Label;

        return KrangleService.KrangleName(conversation.Label);
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
        if (!IsDirectMessageConversation(configuredConversation.Key))
            return !configuredConversation.Key.Equals(ChatChannelMapper.DirectMessageComposerKey, StringComparison.OrdinalIgnoreCase) &&
                   !IsGeneralConversationHidden(configuredConversation.Key);

        return plugin.Configuration.SelectedOutgoingChannel == OutgoingChannel.Tell &&
               !string.IsNullOrWhiteSpace(plugin.Configuration.TellTarget);
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

        var overflowConversations = directMessageConversations
            .Where(conversation => !pendingDirectMessageTabs.ContainsKey(conversation.Key))
            .Where(conversation => !IsPinnedDirectMessageConversation(conversation.Key))
            .Skip(DefaultVisibleDirectMessageTabs);

        foreach (var conversation in overflowConversations)
            closedConversationCutoffs[conversation.Key] = conversation.LastMessageUtc;
    }

    private void CloseConversation(ConversationTabState conversation, bool isPinnedDirectMessage)
    {
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
        var configuration = plugin.Configuration;
        switch (conversationKey)
        {
            case "channel:ECHO":
                return SetOutgoingChannel(configuration, OutgoingChannel.Echo);
            case "channel:SAY":
                return SetOutgoingChannel(configuration, OutgoingChannel.Say);
            case "channel:PARTY":
                return SetOutgoingChannel(configuration, OutgoingChannel.Party);
            case "channel:ALLIANCE":
                return SetOutgoingChannel(configuration, OutgoingChannel.Alliance);
            case "channel:FC":
                return SetOutgoingChannel(configuration, OutgoingChannel.FreeCompany);
            case "channel:SHOUT":
                return SetOutgoingChannel(configuration, OutgoingChannel.Shout);
            case "channel:YELL":
                return SetOutgoingChannel(configuration, OutgoingChannel.Yell);
        }

        if (conversationKey.StartsWith("channel:LS", StringComparison.OrdinalIgnoreCase))
        {
            var slot = ParseConversationSlot(conversationKey, "channel:LS");
            var changed = SetOutgoingChannel(configuration, OutgoingChannel.Linkshell);
            if (slot.HasValue && configuration.LinkshellSlot != slot.Value)
            {
                configuration.LinkshellSlot = slot.Value;
                changed = true;
            }

            return changed;
        }

        if (conversationKey.StartsWith("channel:CWLS", StringComparison.OrdinalIgnoreCase))
        {
            var slot = ParseConversationSlot(conversationKey, "channel:CWLS");
            var changed = SetOutgoingChannel(configuration, OutgoingChannel.CrossWorldLinkshell);
            if (slot.HasValue && configuration.CrossWorldLinkshellSlot != slot.Value)
            {
                configuration.CrossWorldLinkshellSlot = slot.Value;
                changed = true;
            }

            return changed;
        }

        return false;
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
        => GetAllGeneralConversations()
            .Where(conversation => !IsGeneralConversationHidden(conversation.Key))
            .ToList();

    private List<ConversationTabState> GetAllGeneralConversations()
    {
        var configuration = plugin.Configuration;
        var conversations = new List<ConversationTabState>
        {
            BuildPinnedConversation("channel:ECHO", "Echo"),
            BuildPinnedConversation("channel:PROGRESS", "Progress"),
            BuildPinnedConversation("channel:COMBAT", "Combat"),
            BuildPinnedConversation("channel:SAY", "Say"),
            BuildPinnedConversation("channel:PARTY", "Party"),
            BuildPinnedConversation("channel:ALLIANCE", "Alliance"),
            BuildPinnedConversation("channel:FC", "FC"),
            BuildPinnedConversation("channel:SHOUT", "Shout"),
            BuildPinnedConversation("channel:YELL", "Yell"),
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
        plugin.Configuration.SelectedOutgoingChannel = OutgoingChannel.Tell;
        plugin.Configuration.TellTarget = normalizedIdentity;
        plugin.Configuration.Save();

        var conversationKey = ChatChannelMapper.GetDirectMessageConversationKey(normalizedIdentity);
        pendingDirectMessageTabs[conversationKey] = normalizedIdentity;
        closedConversationCutoffs.Remove(conversationKey);
        activeConversationKey = conversationKey;
        activeConversationLabel = normalizedIdentity;
        forceActiveConversationSelection = true;
        requestWindowFocus = true;
        requestSimpleComposerFocus = true;
        ApplySavedPositionForCurrentCharacter();
        IsOpen = true;
    }

    private void HandlePendingPopups()
    {
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
        ClearTransientUiStatus();
        suppressSimpleComposerAutoFocusThisFrame = true;
        pendingDirectMessageTarget = initialTarget ?? string.Empty;
        directMessagePopupError = string.Empty;
        requestDirectMessageTargetFocus = true;
        requestOpenDirectMessagePopup = true;
    }

    private void DrawConversationToolbarButtons()
    {
        var totalWidth = GetConversationToolbarWidth();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, ImGui.GetContentRegionAvail().X - totalWidth));

        if (ImGui.SmallButton("H"))
        {
            ClearTransientUiStatus();
            suppressSimpleComposerAutoFocusThisFrame = true;
            requestOpenHiddenChannelsPopup = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show hidden channel tabs.");

        ImGui.SameLine();
        if (ImGui.SmallButton("R"))
        {
            ClearTransientUiStatus();
            suppressSimpleComposerAutoFocusThisFrame = true;
            requestOpenRecentDirectMessagesPopup = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reopen recent DM tabs.");

        ImGui.SameLine();
        if (ImGui.SmallButton("+"))
            QueueOpenDirectMessagePopup();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open a new DM.");
    }

    private void DrawHiddenChannelsPopup()
    {
        if (!ImGui.BeginPopup(HiddenChannelsPopupId))
            return;

        var hiddenConversations = GetAllGeneralConversations()
            .Where(conversation => IsGeneralConversationHidden(conversation.Key))
            .ToList();

        if (hiddenConversations.Count == 0)
        {
            ImGui.TextDisabled("No channels are hidden.");
            ImGui.EndPopup();
            return;
        }

        foreach (var conversation in hiddenConversations)
        {
            var displayLabel = GetConversationDisplayLabel(conversation);
            if (!ImGui.Selectable(displayLabel, false))
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
        if (!ImGui.BeginPopup(RecentDirectMessagesPopupId))
            return;

        ImGui.SetNextItemWidth(280f);
        ImGui.InputTextWithHint("##RecentDmSearch", "Search recent DMs", ref recentDirectMessageSearch, 128);
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
            ImGui.TextDisabled("No recent DMs matched.");
            ImGui.EndPopup();
            return;
        }

        foreach (var conversation in filteredConversations)
        {
            var isPinned = IsPinnedDirectMessageConversation(conversation.Key);
            var label = isPinned ? $"{GetConversationDisplayLabel(conversation)} [P]" : GetConversationDisplayLabel(conversation);
            if (ImGui.Selectable(label, false))
            {
                ClearTransientUiStatus();
                ReopenDirectMessageConversation(conversation);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"{(isPinned ? "Unpin" : "Pin")}##{conversation.Key}"))
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
        ApplySavedPositionForCurrentCharacter();
        IsOpen = true;
    }

    private void DrawDirectMessageCreationPopup()
    {
        if (!ImGui.BeginPopupModal(NewDirectMessagePopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted("Open a DM by entering First Last@World.");
        if (requestDirectMessageTargetFocus)
        {
            ImGui.SetKeyboardFocusHere();
            requestDirectMessageTargetFocus = false;
        }

        var submit = ImGui.InputTextWithHint(
            "##DhogGPTNewDmTarget",
            "First Last@World",
            ref pendingDirectMessageTarget,
            128,
            ImGuiInputTextFlags.EnterReturnsTrue);
        newDirectMessageTargetInputRect = TrackedInputRect.CaptureCurrentItem();
        newDirectMessageTargetFocusedLastFrame = ImGui.IsItemFocused();

        if (!string.IsNullOrWhiteSpace(directMessagePopupError))
            ImGui.TextColored(new Vector4(1.0f, 0.55f, 0.55f, 1.0f), directMessagePopupError);

        if (submit || ImGui.Button("Open"))
        {
            if (TryConfirmDirectMessagePopup())
                ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
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

        if (conversation.Messages.Count > 0)
        {
            if (ImGui.Selectable(isPinnedDirectMessage ? "Unpin conversation" : "Pin conversation"))
            {
                ClearTransientUiStatus();
                SetPinnedDirectMessageConversation(conversation.Key, conversation.Label, !isPinnedDirectMessage);
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Selectable("Pin conversation");
            ImGui.EndDisabled();
        }

        if (ImGui.Selectable("Copy DM target"))
            ImGui.SetClipboardText(conversation.Label);

        if (ImGui.Selectable(isPinnedDirectMessage ? "Close pinned DM (hold Ctrl + click x)" : "Close DM"))
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

        ImGui.GetWindowDrawList().AddText(new Vector2(pinMin.X + 1f, pinMin.Y - 1f), pinColor, "!");

        if (!ImGui.IsMouseHoveringRect(pinMin, pinMax))
            return;

        ImGui.SetTooltip(isPinnedDirectMessage ? "Pinned DM tab. Click to unpin." : "Click to pin this DM tab.");
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            ClearTransientUiStatus();
            SetPinnedDirectMessageConversation(conversation.Key, conversation.Label, !isPinnedDirectMessage);
        }
    }

    private static bool DrawOutgoingChannelSelectable(string label, bool isSelected, Action onSelected)
    {
        if (!ImGui.Selectable(label, isSelected))
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
        var configuration = plugin.Configuration;
        var isSelected = conversation.Key.Equals(ChatChannelMapper.GetOutgoingConversation(configuration).Key, StringComparison.OrdinalIgnoreCase);
        return DrawOutgoingChannelSelectable(GetConversationDisplayLabel(conversation), isSelected, () =>
        {
            ClearTransientUiStatus();
            TryApplyConversationKeyToOutgoingChannel(conversation.Key);
        });
    }

    private string GetOutgoingConversationDisplayLabel()
    {
        var conversation = ChatChannelMapper.GetOutgoingConversation(plugin.Configuration);
        if (conversation.Key.Equals(ChatChannelMapper.DirectMessageComposerKey, StringComparison.OrdinalIgnoreCase))
            return "New DM";

        return ShellChannelDisplayService.GetDisplayLabel(plugin.Configuration, conversation.Key, conversation.Label);
    }

    private void DrawGeneralConversationContextMenu(ConversationTabState conversation)
    {
        if (!ImGui.BeginPopupContextItem($"DhogGPTGeneralConversationContext##{conversation.Key}"))
            return;

        if (ShellChannelDisplayService.TryGetDescriptor(conversation.Key, out _))
        {
            var useTechnical = ShellChannelDisplayService.UsesTechnicalLabel(plugin.Configuration, conversation.Key);
            if (ImGui.Selectable("Use in-game name", !useTechnical))
            {
                if (ShellChannelDisplayService.SetUseTechnicalLabel(plugin.Configuration, conversation.Key, false))
                    plugin.Configuration.Save();
            }

            if (ImGui.Selectable("Use technical name", useTechnical))
            {
                if (ShellChannelDisplayService.SetUseTechnicalLabel(plugin.Configuration, conversation.Key, true))
                    plugin.Configuration.Save();
            }

            ImGui.Separator();
        }

        ImGui.TextDisabled("Hold Ctrl and click x to hide this channel tab.");
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
        if (!plugin.Configuration.OpenMainWindowOnIncomingDirectMessage)
            return;

        _ = Plugin.Framework.RunOnFrameworkThread(() => OpenDirectMessageConversation(identity));
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
            var draft = plugin.Configuration.OutgoingDraft;
            if (!draft.StartsWith("/", StringComparison.Ordinal))
                plugin.Configuration.OutgoingDraft = "/";
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
            ChannelLabel = "Echo",
            Sender = sender,
            ConversationKey = "channel:ECHO",
            ConversationLabel = "Echo",
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
        activeConversationLabel = "Echo";
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
           !newDirectMessageTargetFocusedLastFrame &&
           !anyPopupOpenLastFrame;

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
        var textSize = ImGui.CalcTextSize(indicator);
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
        drawList.AddText(position, ImGui.GetColorU32(new Vector4(0.78f, 0.78f, 0.78f, 0.95f)), indicator);
    }

    private static string BuildScrollIndicator(char direction, float width)
    {
        var segment = $"{direction} ";
        var segmentWidth = Math.Max(1f, ImGui.CalcTextSize(segment).X);
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

    private float GetActiveWindowOpacity()
    {
        var focusedOpacity = Math.Clamp(plugin.Configuration.FocusedWindowOpacity, 0.20f, 1.0f);
        var backgroundOpacity = Math.Clamp(plugin.Configuration.BackgroundWindowOpacity, 0.20f, 1.0f);
        return useFocusedWindowOpacity
            ? Math.Max(focusedOpacity, backgroundOpacity)
            : Math.Min(backgroundOpacity, focusedOpacity);
    }

    private float GetInactiveSimpleComposerOpacity()
        => Math.Clamp(plugin.Configuration.WindowOpacity, 0.20f, 1.0f);

    private void UpdateWindowOpacityState()
    {
        windowHoveredLastFrame = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        windowFocusedLastFrame = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        useFocusedWindowOpacity = windowHoveredLastFrame || windowFocusedLastFrame;
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

            totalWidth += ImGui.CalcTextSize(label).X;
            totalWidth += (style.FramePadding.X * 2f) + closeButtonWidth + style.ItemInnerSpacing.X + style.ItemSpacing.X;
        }

        return totalWidth;
    }

    private bool WillConversationTabBarOverflow(IReadOnlyList<ConversationTabState> conversations, float availableWidth)
        => availableWidth > 0f && EstimateConversationTabBarWidth(conversations) > availableWidth;

    private float GetConversationToolbarWidth()
    {
        var style = ImGui.GetStyle();
        var labels = new[] { "H", "R", "+" };
        var totalWidth = 0f;

        foreach (var label in labels)
            totalWidth += ImGui.CalcTextSize(label).X + (style.FramePadding.X * 2f);

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
        plugin.SaveCurrentWindowPosition(false, currentPosition);
    }

    private (Vector4 Header, Vector4 Translation, Vector4 Error) GetMessagePalette(bool isInbound)
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

        var inbound = GetMessagePalette(isInbound: true).Header;
        var outbound = GetMessagePalette(isInbound: false).Header;
        var accent = BlendColors(inbound, outbound, 0.5f);
        var tab = WithAlpha(ScaleColorRgb(accent, 0.48f), 0.78f);
        var hovered = WithAlpha(ScaleColorRgb(accent, 0.90f), 0.92f);
        var active = WithAlpha(ScaleColorRgb(accent, 1.12f), 0.98f);
        var unfocused = WithAlpha(ScaleColorRgb(accent, 0.36f), 0.55f);
        var unfocusedActive = WithAlpha(ScaleColorRgb(accent, 0.72f), 0.78f);
        return (tab, hovered, active, unfocused, unfocusedActive, GetReadableTextColor(tab), GetReadableTextColor(active));
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
