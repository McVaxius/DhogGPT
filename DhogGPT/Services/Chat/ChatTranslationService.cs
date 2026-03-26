using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using DhogGPT.Models;
using DhogGPT.Services.Translation;

namespace DhogGPT.Services.Chat;

public sealed class ChatTranslationService : IDisposable
{
    private readonly Configuration configuration;
    private readonly TranslationCoordinator translationCoordinator;
    private readonly Dictionary<string, DateTimeOffset> recentMessages = [];
    private readonly object syncRoot = new();

    public ChatTranslationService(Configuration configuration, TranslationCoordinator translationCoordinator)
    {
        this.configuration = configuration;
        this.translationCoordinator = translationCoordinator;

        translationCoordinator.InboundTranslationReady += OnInboundTranslationReady;
        Plugin.ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        translationCoordinator.InboundTranslationReady -= OnInboundTranslationReady;
        Plugin.ChatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!configuration.PluginEnabled || !configuration.TranslateIncoming)
            return;

        if (!ChatChannelMapper.TryGetIncomingChannelLabel(configuration, type, out var channelLabel))
            return;

        var messageText = message.TextValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        var senderText = sender.TextValue?.Trim() ?? string.Empty;
        if (configuration.SkipOwnMessages && IsLocalPlayer(senderText))
            return;

        if (!ShouldQueue(channelLabel, senderText, messageText))
            return;

        var request = new TranslationRequest
        {
            Text = messageText,
            SourceLanguage = configuration.IncomingSourceLanguage,
            TargetLanguage = configuration.IncomingTargetLanguage,
            IsInbound = true,
            Sender = senderText,
            ChannelLabel = channelLabel,
        };

        _ = translationCoordinator.QueueIncomingAsync(request);
    }

    private void OnInboundTranslationReady(TranslationResult result)
    {
        if (!result.HasMeaningfulTranslation)
            return;

        var parts = new List<string> { "[DhogGPT]" };

        if (configuration.IncludeChannelLabel && !string.IsNullOrWhiteSpace(result.Request.ChannelLabel))
            parts.Add($"[{result.Request.ChannelLabel}]");

        if (configuration.IncludeSenderName && !string.IsNullOrWhiteSpace(result.Request.Sender))
            parts.Add($"[{result.Request.Sender}]");

        var translatedText = result.TranslatedText.Trim();
        var output = string.Join(string.Empty, parts) + " " + translatedText;

        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            Plugin.ChatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Echo,
                Message = new SeString(new TextPayload(output)),
            });
        });
    }

    private bool ShouldQueue(string channelLabel, string senderText, string messageText)
    {
        lock (syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddSeconds(-10);
            var expiredKeys = recentMessages.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray();
            foreach (var key in expiredKeys)
            {
                recentMessages.Remove(key);
            }

            var dedupeKey = $"{channelLabel}::{Normalize(senderText)}::{Normalize(messageText)}";
            if (recentMessages.ContainsKey(dedupeKey))
                return false;

            recentMessages[dedupeKey] = now;
            return true;
        }
    }

    private static bool IsLocalPlayer(string senderText)
    {
        var localName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;
        if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(senderText))
            return false;

        return Normalize(senderText).Contains(Normalize(localName), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim();
        var atIndex = trimmed.IndexOf('@');
        if (atIndex >= 0)
            trimmed = trimmed[..atIndex];

        return string.Join(" ", trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
