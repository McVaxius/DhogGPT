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

    private bool previewBusy;
    private string previewStatus = string.Empty;
    private string previewText = string.Empty;
    private string previewMetadata = string.Empty;

    public MainWindow(
        Plugin plugin,
        LanguageRegistryService languageRegistry,
        TranslationCoordinator translationCoordinator,
        SessionHealthService sessionHealth)
        : base("DhogGPT###DhogGPTMain")
    {
        this.plugin = plugin;
        this.languageRegistry = languageRegistry;
        this.translationCoordinator = translationCoordinator;
        this.sessionHealth = sessionHealth;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720f, 520f),
            MaximumSize = new Vector2(1400f, 1000f),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();
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

        ImGui.Text($"{Plugin.DisplayName} v{version}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 255f);
        if (ImGui.SmallButton("Ko-fi"))
            Process.Start(new ProcessStartInfo { FileName = Plugin.SupportUrl, UseShellExecute = true });

        ImGui.SameLine();
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

        ImGui.TextWrapped("Incoming chat is translated in the background and echoed back with labels. Outgoing translation uses the composer below so you stay in control of what gets sent.");
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

        if (DrawOutgoingChannelCombo())
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
            var configuration = plugin.Configuration;
            var request = new TranslationRequest
            {
                Text = configuration.OutgoingDraft,
                SourceLanguage = configuration.OutgoingSourceLanguage,
                TargetLanguage = configuration.OutgoingTargetLanguage,
                IsInbound = false,
                ChannelLabel = ChatChannelMapper.GetOutgoingLabel(configuration),
                Sender = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
            };

            var result = await translationCoordinator.TranslateImmediatelyAsync(request).ConfigureAwait(false);
            if (!result.Success)
            {
                previewStatus = $"Translation failed: {result.Error}";
                return;
            }

            var sourceDisplay = !string.IsNullOrWhiteSpace(result.DetectedSourceLanguage)
                ? languageRegistry.GetName(result.DetectedSourceLanguage)
                : languageRegistry.GetName(result.Request.SourceLanguage);

            previewStatus = result.FromCache
                ? "Preview ready from cache."
                : "Preview ready.";
            previewText = result.TranslatedText;
            previewMetadata = $"Source: {sourceDisplay}  Target: {languageRegistry.GetName(result.Request.TargetLanguage)}  Provider: {result.ProviderName} ({result.Endpoint})";

            if (!sendAfterTranslate)
                return;

            if (!CommandHelper.TryBuildOutgoingCommand(configuration, result.TranslatedText, out var command, out var error))
            {
                previewStatus = error;
                return;
            }

            var sent = await Plugin.Framework.RunOnFrameworkThread(() => CommandHelper.SendCommand(command)).ConfigureAwait(false);
            previewStatus = sent
                ? $"Sent translated message to {ChatChannelMapper.GetOutgoingLabel(configuration)}."
                : "Translation succeeded, but sending the message failed.";
        }
        catch (OperationCanceledException)
        {
            previewStatus = "Translation was cancelled.";
        }
        catch (Exception ex)
        {
            previewStatus = $"Unexpected error: {ex.Message}";
            Plugin.Log.Error($"[DhogGPT] Preview/send failed: {ex.Message}");
        }
        finally
        {
            previewBusy = false;
        }
    }

    private bool DrawOutgoingChannelCombo()
    {
        var changed = false;
        var configuration = plugin.Configuration;
        var selectedLabel = ChatChannelMapper.GetOutgoingLabel(configuration);

        if (!ImGui.BeginCombo("Channel", selectedLabel))
            return false;

        foreach (var channel in Enum.GetValues<OutgoingChannel>())
        {
            var label = channel switch
            {
                OutgoingChannel.FreeCompany => "Free Company",
                OutgoingChannel.CrossWorldLinkshell => "Cross-world Linkshell",
                _ => channel.ToString(),
            };

            var isSelected = channel == configuration.SelectedOutgoingChannel;
            if (ImGui.Selectable(label, isSelected))
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
}
