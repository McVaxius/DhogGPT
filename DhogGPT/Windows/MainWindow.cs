using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DhogGPT.Models;
using DhogGPT.Services;
using DhogGPT.Services.Chat;
using DhogGPT.Services.Diagnostics;
using DhogGPT.Services.Translation;

namespace DhogGPT.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly LanguageRegistryService languageRegistry;
    private readonly TranslationCoordinator translationCoordinator;
    private readonly SessionHealthService sessionHealth;
    private readonly ChatLogService chatLogService;
    private readonly Dictionary<string, DateTimeOffset> closedConversationCutoffs = new(StringComparer.OrdinalIgnoreCase);

    private bool previewBusy;
    private string previewStatus = string.Empty;
    private string previewText = string.Empty;
    private string previewMetadata = string.Empty;
    private string simpleChatStatus = string.Empty;
    private string activeConversationKey = string.Empty;
    private string activeConversationLabel = string.Empty;
    private bool requestSimpleComposerFocus;
    private bool forceActiveConversationSelection;

    public MainWindow(
        Plugin plugin,
        LanguageRegistryService languageRegistry,
        TranslationCoordinator translationCoordinator,
        SessionHealthService sessionHealth,
        ChatLogService chatLogService)
        : base("DhogGPT###DhogGPTMain")
    {
        this.plugin = plugin;
        this.languageRegistry = languageRegistry;
        this.translationCoordinator = translationCoordinator;
        this.sessionHealth = sessionHealth;
        this.chatLogService = chatLogService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720f, 520f),
            MaximumSize = new Vector2(1400f, 1000f),
        };
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(Math.Clamp(plugin.Configuration.WindowOpacity, 0.35f, 1.0f));
    }

    public override void Draw()
    {
        if (plugin.Configuration.UseSimpleChatMode && plugin.Configuration.CompactSimpleChatMode)
        {
            DrawCompactHeader();
        }
        else
        {
            DrawHeader();
        }

        ImGui.Separator();

        if (plugin.Configuration.UseSimpleChatMode)
        {
            DrawSimpleChatMode();
            return;
        }

        DrawStatusPanel();
        ImGui.Separator();
        DrawComposer();
        ImGui.Separator();
        DrawHistory();
    }

    private void DrawHeader()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        var configuration = plugin.Configuration;
        var headerText = $"{Plugin.DisplayName} v{version}";
        var koFiWidth = ImGui.CalcTextSize("Ko-fi").X + (ImGui.GetStyle().FramePadding.X * 2f);

        if (ImGui.BeginTable("DhogGPTHeaderTop", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Support", ImGuiTableColumnFlags.WidthFixed, koFiWidth + 8f);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(headerText);

            ImGui.TableSetColumnIndex(1);
            if (ImGui.SmallButton("Ko-fi"))
                Process.Start(new ProcessStartInfo { FileName = Plugin.SupportUrl, UseShellExecute = true });

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
        var simpleChatMode = configuration.UseSimpleChatMode;
        if (ImGui.Checkbox("Simple chat mode", ref simpleChatMode))
        {
            configuration.UseSimpleChatMode = simpleChatMode;
            configuration.Save();
        }

        if (configuration.UseSimpleChatMode)
        {
            ImGui.SameLine();
            var compactSimpleChat = configuration.CompactSimpleChatMode;
            if (ImGui.Checkbox("Compact", ref compactSimpleChat))
            {
                configuration.CompactSimpleChatMode = compactSimpleChat;
                configuration.Save();
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(configuration.KrangleChatNames ? "Krangle Names: On" : "Krangle Names: Off"))
        {
            configuration.KrangleChatNames = !configuration.KrangleChatNames;
            configuration.Save();
        }

        ImGui.TextWrapped(configuration.UseSimpleChatMode
            ? "Simple chat mode keeps translated conversations in tabs by channel or DM, with a compact composer at the bottom."
            : "Incoming chat is translated in the background and echoed back with labels. Outgoing translation uses the composer below so you stay in control of what gets sent.");
    }

    private void DrawCompactHeader()
    {
        var configuration = plugin.Configuration;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        if (!ImGui.BeginTable("DhogGPTCompactHeader", 2, ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthFixed, 370f);
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled($"{Plugin.DisplayName} v{version}");

        ImGui.TableSetColumnIndex(1);
        var enabled = configuration.PluginEnabled;
        if (ImGui.Checkbox("Enabled##CompactHeader", ref enabled))
            plugin.SetPluginEnabled(enabled);

        ImGui.SameLine();
        var compact = configuration.CompactSimpleChatMode;
        if (ImGui.Checkbox("Compact##CompactHeader", ref compact))
        {
            configuration.CompactSimpleChatMode = compact;
            configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(configuration.KrangleChatNames ? "Krangle: On" : "Krangle: Off"))
        {
            configuration.KrangleChatNames = !configuration.KrangleChatNames;
            configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        if (ImGui.SmallButton("Ko-fi"))
            Process.Start(new ProcessStartInfo { FileName = Plugin.SupportUrl, UseShellExecute = true });

        ImGui.EndTable();
    }

    private void DrawSimpleChatMode()
    {
        DrawSimpleLanguageBar();
        ImGui.Separator();

        var composerHeight = ImGui.GetFrameHeightWithSpacing() * 2.6f;
        var chatBodyHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - composerHeight);
        DrawTabbedConversationArea(chatBodyHeight);

        ImGui.Separator();
        DrawSimpleComposer();
    }

    private void DrawSimpleLanguageBar()
    {
        var configuration = plugin.Configuration;
        var changed = false;

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
        var recordedConversations = entries
            .GroupBy(GetConversationGroupingKey)
            .Select(group =>
            {
                var messages = group.OrderBy(entry => entry.TimestampUtc).ToList();
                return new ConversationTabState(
                    group.Key,
                    ResolveConversationLabel(messages),
                    messages,
                    messages.Count > 0 ? messages[^1].TimestampUtc : DateTimeOffset.MinValue);
            })
            .OrderByDescending(state => state.LastMessageUtc)
            .ToList();

        var conversations = new List<ConversationTabState>();
        var remainingConversations = recordedConversations.ToDictionary(state => state.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var pinnedConversation in GetPinnedGeneralConversations())
        {
            if (remainingConversations.Remove(pinnedConversation.Key, out var existingConversation))
                conversations.Add(existingConversation);
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

        var directMessageConversations = remainingConversations.Values
            .Where(IsConversationVisible)
            .Where(conversation => IsDirectMessageConversation(conversation.Key))
            .OrderByDescending(conversation => conversation.LastMessageUtc)
            .ToList();

        conversations.AddRange(directMessageConversations);

        if (string.IsNullOrWhiteSpace(activeConversationKey) || conversations.All(state => !state.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase)))
        {
            activeConversationKey = conversations.FirstOrDefault()?.Key ?? string.Empty;
            activeConversationLabel = conversations.FirstOrDefault()?.Label ?? string.Empty;
            forceActiveConversationSelection = true;
        }

        if (!ImGui.BeginTabBar("DhogGPTConversationTabs"))
            return;

        foreach (var conversation in conversations)
        {
            var displayLabel = GetConversationDisplayLabel(conversation);
            var isDirectMessage = IsDirectMessageConversation(conversation.Key);
            var tabFlags = conversation.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase) && forceActiveConversationSelection
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var tabOpen = true;
            var tabVisible = isDirectMessage
                ? ImGui.BeginTabItem($"{displayLabel}##{conversation.Key}", ref tabOpen, tabFlags)
                : ImGui.BeginTabItem($"{displayLabel}##{conversation.Key}", tabFlags);

            if (!tabVisible)
            {
                if (isDirectMessage && !tabOpen)
                    CloseConversation(conversation);

                continue;
            }

            activeConversationKey = conversation.Key;
            activeConversationLabel = conversation.Label;
            if (SyncOutgoingChannelToConversation(conversation))
                plugin.Configuration.Save();
            DrawConversationMessages(conversation.Messages, height);
            ImGui.EndTabItem();

            if (isDirectMessage && !tabOpen)
                CloseConversation(conversation);
        }

        ImGui.EndTabBar();
        forceActiveConversationSelection = false;
    }

    private void DrawConversationMessages(IReadOnlyList<TranslationHistoryItem> messages, float height)
    {
        if (!ImGui.BeginChild("DhogGPTConversationBody", new Vector2(0f, height), true))
        {
            ImGui.EndChild();
            return;
        }

        if (messages.Count == 0)
        {
            ImGui.TextDisabled("No translated chat has been logged for this tab yet.");
            ImGui.EndChild();
            return;
        }

        var originalSpacing = ImGui.GetStyle().ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(originalSpacing.X, 1f));

        foreach (var message in messages.OrderBy(entry => entry.TimestampUtc))
        {
            var (headerColor, translatedColor, errorColor) = GetMessagePalette(message.IsInbound);
            var timestamp = message.TimestampUtc.ToLocalTime().ToString("HH:mm");
            var displayName = GetDisplayName(message);
            var originalText = string.IsNullOrWhiteSpace(message.OriginalText) ? "(empty)" : message.OriginalText;

            ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
            ImGui.TextWrapped($"{timestamp} - {displayName} - {originalText}");
            ImGui.PopStyleColor();

            if (message.Success)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, translatedColor);
                ImGui.TextWrapped(message.TranslatedText);
                ImGui.PopStyleColor();
            }
            else if (!string.IsNullOrWhiteSpace(message.Error))
            {
                ImGui.TextColored(errorColor, $"Translation failed: {message.Error}");
            }

            ImGui.Dummy(new Vector2(0f, 2f));
        }

        ImGui.PopStyleVar();
        ImGui.EndChild();
    }

    private void DrawSimpleComposer()
    {
        var configuration = plugin.Configuration;
        var changed = SyncSimpleComposerToActiveConversation();

        if (configuration.SelectedOutgoingChannel == OutgoingChannel.Tell &&
            (!IsDirectMessageConversation(activeConversationKey) ||
             string.IsNullOrWhiteSpace(activeConversationLabel) ||
             string.Equals(activeConversationLabel, "DM", StringComparison.OrdinalIgnoreCase)))
        {
            var tellTarget = configuration.TellTarget;
            if (ImGui.InputTextWithHint("Tell target##Simple", "First Last@World", ref tellTarget, 128))
            {
                configuration.TellTarget = tellTarget;
                changed = true;
            }
        }

        var comboWidth = 150f;
        var sendWidth = 70f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var entryWidth = Math.Max(120f, ImGui.GetContentRegionAvail().X - comboWidth - sendWidth - (spacing * 2f));
        var submitFromEnter = false;

        ImGui.SetNextItemWidth(comboWidth);
        if (DrawOutgoingChannelCombo("##SimpleChannel"))
        {
            changed = true;
            SyncActiveConversationToOutgoingChannel();
        }

        ImGui.SameLine();
        if (requestSimpleComposerFocus)
        {
            ImGui.SetKeyboardFocusHere();
            requestSimpleComposerFocus = false;
        }

        ImGui.SetNextItemWidth(entryWidth);
        var draft = configuration.OutgoingDraft;
        submitFromEnter = ImGui.InputTextWithHint(
            "##SimpleChatEntry",
            "Translate this text and press Enter to send",
            ref draft,
            2000,
            ImGuiInputTextFlags.EnterReturnsTrue);
        if (draft != configuration.OutgoingDraft)
        {
            configuration.OutgoingDraft = draft;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Send##SimpleSend", new Vector2(sendWidth, 0f)))
            submitFromEnter = true;

        if (submitFromEnter)
            _ = SendSimpleChatAsync();

        if (changed)
            configuration.Save();

        if (!string.IsNullOrWhiteSpace(simpleChatStatus))
            ImGui.TextColored(new Vector4(1.0f, 0.62f, 0.62f, 1.0f), simpleChatStatus);
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

        if (configuration.SelectedOutgoingChannel == OutgoingChannel.Tell)
        {
            var tellTarget = configuration.TellTarget;
            if (ImGui.InputTextWithHint("Tell target", "First Last@World", ref tellTarget, 128))
            {
                configuration.TellTarget = tellTarget;
                changed = true;
            }
        }

        if (configuration.SelectedOutgoingChannel == OutgoingChannel.Linkshell)
        {
            var linkshellSlot = configuration.LinkshellSlot;
            if (ImGui.SliderInt("Linkshell slot", ref linkshellSlot, 1, 8))
            {
                configuration.LinkshellSlot = linkshellSlot;
                changed = true;
            }
        }

        if (configuration.SelectedOutgoingChannel == OutgoingChannel.CrossWorldLinkshell)
        {
            var crossWorldSlot = configuration.CrossWorldLinkshellSlot;
            if (ImGui.SliderInt("CWLS slot", ref crossWorldSlot, 1, 8))
            {
                configuration.CrossWorldLinkshellSlot = crossWorldSlot;
                changed = true;
            }
        }

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
    }

    private void DrawHistory()
    {
        ImGui.TextUnformatted("Recent translations");
        if (!ImGui.BeginChild("DhogGPTHistory", new Vector2(0f, 0f), true))
        {
            ImGui.EndChild();
            return;
        }

        var history = translationCoordinator.GetHistorySnapshot();
        if (history.Count == 0)
        {
            ImGui.TextUnformatted("No translations yet.");
            ImGui.EndChild();
            return;
        }

        foreach (var item in history)
        {
            var direction = item.IsInbound ? "IN" : "OUT";
            var status = item.Success ? "OK" : "ERR";
            var channel = string.IsNullOrWhiteSpace(item.ChannelLabel) ? "-" : item.ChannelLabel;
            var sender = string.IsNullOrWhiteSpace(item.Sender) ? "-" : item.Sender;

            ImGui.TextWrapped($"[{direction}] [{status}] [{channel}] [{sender}] {item.OriginalText}");

            if (item.Success)
                ImGui.TextWrapped($" -> {item.TranslatedText}");
            else
                ImGui.TextWrapped($" -> {item.Error}");

            ImGui.Separator();
        }

        ImGui.EndChild();
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
            var request = BuildOutgoingRequest(recordInHistory: sendAfterTranslate);
            var result = await translationCoordinator.TranslateImmediatelyAsync(request);
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

            if (!CommandHelper.TryBuildOutgoingCommand(plugin.Configuration, result.TranslatedText, out var command, out var error))
            {
                await SetPreviewStateAsync(status: error);
                return;
            }

            var sent = await Plugin.Framework.RunOnFrameworkThread(() => CommandHelper.SendCommand(command));
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
            var request = BuildOutgoingRequest(recordInHistory: true);
            var result = await translationCoordinator.TranslateImmediatelyAsync(request);
            if (!result.Success)
            {
                await SetSimpleChatStatusAsync($"Translation failed: {result.Error}");
                return;
            }

            if (!CommandHelper.TryBuildOutgoingCommand(plugin.Configuration, result.TranslatedText, out var command, out var error))
            {
                await SetSimpleChatStatusAsync(error);
                return;
            }

            var sent = await Plugin.Framework.RunOnFrameworkThread(() => CommandHelper.SendCommand(command));
            if (!sent)
            {
                await SetSimpleChatStatusAsync("Translation succeeded, but sending the message failed.");
                return;
            }

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

    private bool DrawOutgoingChannelCombo(string label)
    {
        var changed = false;
        var configuration = plugin.Configuration;
        var selectedLabel = ChatChannelMapper.GetOutgoingLabel(configuration);

        if (!ImGui.BeginCombo(label, selectedLabel))
            return false;

        foreach (var channel in Enum.GetValues<OutgoingChannel>())
        {
            var channelLabel = channel switch
            {
                OutgoingChannel.FreeCompany => "Free Company",
                OutgoingChannel.CrossWorldLinkshell => "Cross-world Linkshell",
                _ => channel.ToString(),
            };

            var isSelected = channel == configuration.SelectedOutgoingChannel;
            if (ImGui.Selectable(channelLabel, isSelected))
            {
                configuration.SelectedOutgoingChannel = channel;
                changed = true;
            }

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

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
            return true;

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

        if (!closedConversationCutoffs.TryGetValue(conversation.Key, out var cutoff))
            return true;

        if (conversation.LastMessageUtc > cutoff)
        {
            closedConversationCutoffs.Remove(conversation.Key);
            return true;
        }

        return false;
    }

    private void CloseConversation(ConversationTabState conversation)
    {
        closedConversationCutoffs[conversation.Key] = conversation.LastMessageUtc;
        if (conversation.Key.Equals(activeConversationKey, StringComparison.OrdinalIgnoreCase))
        {
            activeConversationKey = string.Empty;
            activeConversationLabel = string.Empty;
        }
    }

    private bool SyncSimpleComposerToActiveConversation()
    {
        if (!IsDirectMessageConversation(activeConversationKey) ||
            string.IsNullOrWhiteSpace(activeConversationLabel) ||
            string.Equals(activeConversationLabel, "DM", StringComparison.OrdinalIgnoreCase))
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
        activeConversationKey = conversation.Key;
        activeConversationLabel = conversation.Label;
        forceActiveConversationSelection = true;
    }

    private bool SyncOutgoingChannelToConversation(ConversationTabState conversation)
    {
        var configuration = plugin.Configuration;
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

        var targetChannel = conversation.Key switch
        {
            "channel:SAY" => OutgoingChannel.Say,
            "channel:PARTY" => OutgoingChannel.Party,
            "channel:ALLIANCE" => OutgoingChannel.Alliance,
            "channel:FC" => OutgoingChannel.FreeCompany,
            "channel:SHOUT" => OutgoingChannel.Shout,
            "channel:YELL" => OutgoingChannel.Yell,
            _ when conversation.Key.StartsWith("channel:LS", StringComparison.OrdinalIgnoreCase) => OutgoingChannel.Linkshell,
            _ when conversation.Key.StartsWith("channel:CWLS", StringComparison.OrdinalIgnoreCase) => OutgoingChannel.CrossWorldLinkshell,
            _ => configuration.SelectedOutgoingChannel,
        };

        if (configuration.SelectedOutgoingChannel == targetChannel)
            return false;

        configuration.SelectedOutgoingChannel = targetChannel;
        return true;
    }

    private List<ConversationTabState> GetPinnedGeneralConversations()
    {
        var configuration = plugin.Configuration;
        return
        [
            BuildPinnedConversation(OutgoingChannel.Say, "Say"),
            BuildPinnedConversation(OutgoingChannel.Party, "Party"),
            BuildPinnedConversation(OutgoingChannel.Alliance, "Alliance"),
            BuildPinnedConversation(OutgoingChannel.FreeCompany, "FC"),
            BuildPinnedConversation(OutgoingChannel.Linkshell, $"LS{Math.Clamp(configuration.LinkshellSlot, 1, 8)}"),
            BuildPinnedConversation(OutgoingChannel.CrossWorldLinkshell, $"CWLS{Math.Clamp(configuration.CrossWorldLinkshellSlot, 1, 8)}"),
            BuildPinnedConversation(OutgoingChannel.Shout, "Shout"),
            BuildPinnedConversation(OutgoingChannel.Yell, "Yell"),
        ];
    }

    private ConversationTabState BuildPinnedConversation(OutgoingChannel channel, string label)
    {
        var key = channel switch
        {
            OutgoingChannel.Say => "channel:SAY",
            OutgoingChannel.Party => "channel:PARTY",
            OutgoingChannel.Alliance => "channel:ALLIANCE",
            OutgoingChannel.FreeCompany => "channel:FC",
            OutgoingChannel.Linkshell => $"channel:LS{Math.Clamp(plugin.Configuration.LinkshellSlot, 1, 8)}",
            OutgoingChannel.CrossWorldLinkshell => $"channel:CWLS{Math.Clamp(plugin.Configuration.CrossWorldLinkshellSlot, 1, 8)}",
            OutgoingChannel.Shout => "channel:SHOUT",
            OutgoingChannel.Yell => "channel:YELL",
            _ => "channel:UNKNOWN",
        };

        return new ConversationTabState(key, label, new List<TranslationHistoryItem>(), DateTimeOffset.MinValue);
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

    private static bool IsDirectMessageConversation(string conversationKey)
        => conversationKey.StartsWith("dm:", StringComparison.OrdinalIgnoreCase);

    private sealed record ConversationTabState(
        string Key,
        string Label,
        IReadOnlyList<TranslationHistoryItem> Messages,
        DateTimeOffset LastMessageUtc);
}
