using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using DhogGPT.Models;
using DhogGPT.Services.Translation;

namespace DhogGPT.Services.Chat;

public sealed class ChatTranslationService : IDisposable
{
    private static readonly TimeSpan PendingOutgoingDirectMessageConfirmDelay = TimeSpan.FromSeconds(1.5);
    private static readonly string[] DirectMessageErrorSnippets =
    [
        "There are no Worlds by that name",
        "To use that command, you must add the World name",
        "The specified PC could not be found",
        "Unable to send a /tell",
        "Unable to use /tell",
    ];

    private readonly Configuration configuration;
    private readonly TranslationCoordinator translationCoordinator;
    private readonly Dictionary<string, DateTimeOffset> recentMessages = [];
    private readonly List<PendingOutgoingDirectMessage> pendingOutgoingDirectMessages = [];
    private readonly List<ConfirmedOutgoingDirectMessage> confirmedOutgoingDirectMessages = [];
    private readonly object syncRoot = new();

    public ChatTranslationService(Configuration configuration, TranslationCoordinator translationCoordinator)
    {
        this.configuration = configuration;
        this.translationCoordinator = translationCoordinator;

        translationCoordinator.InboundTranslationReady += OnInboundTranslationReady;
        Plugin.ChatGui.ChatMessage += OnChatMessage;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        translationCoordinator.InboundTranslationReady -= OnInboundTranslationReady;
        Plugin.ChatGui.ChatMessage -= OnChatMessage;
        Plugin.Framework.Update -= OnFrameworkUpdate;
    }

    public event Action<string>? IncomingDirectMessageObserved;

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        var messageText = message.TextValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        if (LooksLikeDirectMessageSendError(messageText))
        {
            CancelMostRecentPendingOutgoingDirectMessage();
            return;
        }

        var senderText = ResolveSenderIdentity(sender);
        var normalizedSender = ChatChannelMapper.NormalizeDirectMessageLabel(senderText);

        if (type == XivChatType.TellOutgoing)
        {
            TryConfirmPendingOutgoingDirectMessage(normalizedSender, messageText);
            return;
        }

        if (type == XivChatType.TellIncoming)
        {
            if (TryConfirmPendingOutgoingDirectMessage(normalizedSender, messageText))
                return;

            if (configuration.PluginEnabled &&
                !string.IsNullOrWhiteSpace(normalizedSender) &&
                !string.Equals(normalizedSender, "DM", StringComparison.OrdinalIgnoreCase))
            {
                ChatChannelMapper.RegisterKnownDirectMessageIdentity(normalizedSender);
                IncomingDirectMessageObserved?.Invoke(normalizedSender);
            }
        }

        if (!configuration.PluginEnabled || !configuration.TranslateIncoming)
            return;

        if (!ChatChannelMapper.TryGetIncomingChannelLabel(configuration, type, out var channelLabel))
            return;

        if (type == XivChatType.TellIncoming && ShouldSuppressConfirmedOutgoingEcho(normalizedSender, messageText))
            return;

        if (configuration.SkipOwnMessages && IsLocalPlayer(senderText))
            return;

        if (!ShouldQueue(channelLabel, senderText, messageText))
            return;

        var conversation = ChatChannelMapper.GetIncomingConversation(channelLabel, senderText);
        var request = new TranslationRequest
        {
            Text = messageText,
            OriginalSeStringBase64 = CaptureOriginalSeString(message),
            SourceLanguage = configuration.IncomingSourceLanguage,
            TargetLanguage = configuration.IncomingTargetLanguage,
            IsInbound = true,
            Sender = senderText,
            ChannelLabel = channelLabel,
            ConversationKey = conversation.Key,
            ConversationLabel = conversation.Label,
        };

        _ = translationCoordinator.QueueIncomingAsync(request);
    }

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        List<PendingOutgoingDirectMessage>? autoConfirmed = null;

        lock (syncRoot)
        {
            CleanupPendingOutgoingDirectMessages();
            CleanupConfirmedOutgoingDirectMessages();

            var cutoff = DateTimeOffset.UtcNow - PendingOutgoingDirectMessageConfirmDelay;
            for (var i = pendingOutgoingDirectMessages.Count - 1; i >= 0; i--)
            {
                var pending = pendingOutgoingDirectMessages[i];
                if (pending.CreatedAtUtc > cutoff)
                    continue;

                autoConfirmed ??= [];
                autoConfirmed.Add(pending);
                confirmedOutgoingDirectMessages.Add(new ConfirmedOutgoingDirectMessage(
                    pending.NormalizedConversationLabel,
                    pending.NormalizedTranslatedText,
                    DateTimeOffset.UtcNow));
                pendingOutgoingDirectMessages.RemoveAt(i);
            }
        }

        if (autoConfirmed == null || autoConfirmed.Count == 0)
            return;

        autoConfirmed.Reverse();
        foreach (var pending in autoConfirmed)
            translationCoordinator.RecordTranslationResult(pending.Result);
    }

    public void RegisterPendingOutgoingDirectMessage(TranslationResult result)
    {
        if (!result.Success ||
            result.Request.IsInbound ||
            !string.Equals(result.Request.ChannelLabel, "DM", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (syncRoot)
        {
            CleanupPendingOutgoingDirectMessages();
            pendingOutgoingDirectMessages.Add(new PendingOutgoingDirectMessage(
                result,
                Normalize(result.Request.ConversationLabel),
                Normalize(result.TranslatedText),
                DateTimeOffset.UtcNow));
        }
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
            CleanupPendingOutgoingDirectMessages();
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

    private static string ResolveSenderIdentity(SeString sender)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is PlayerPayload playerPayload)
            {
                var playerName = playerPayload.PlayerName?.Trim() ?? string.Empty;
                var worldName = playerPayload.World.Value.Name.ToString();
                var fullIdentity = ChatChannelMapper.BuildDirectMessageIdentity(playerName, worldName);
                if (!string.IsNullOrWhiteSpace(fullIdentity))
                    return fullIdentity;

                var displayedName = playerPayload.DisplayedName?.Trim();
                if (!string.IsNullOrWhiteSpace(displayedName))
                    return displayedName;
            }
        }

        return sender.TextValue?.Trim() ?? string.Empty;
    }

    private bool TryConfirmPendingOutgoingDirectMessage(string senderText, string messageText)
    {
        TranslationResult? completedResult = null;
        var normalizedSender = Normalize(senderText);
        var normalizedMessage = Normalize(messageText);

        lock (syncRoot)
        {
            CleanupPendingOutgoingDirectMessages();
            CleanupConfirmedOutgoingDirectMessages();

            for (var i = 0; i < pendingOutgoingDirectMessages.Count; i++)
            {
                var pending = pendingOutgoingDirectMessages[i];
                if (!string.Equals(pending.NormalizedTranslatedText, normalizedMessage, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(normalizedSender) &&
                    !string.Equals(pending.NormalizedConversationLabel, normalizedSender, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                completedResult = pending.Result;
                confirmedOutgoingDirectMessages.Add(new ConfirmedOutgoingDirectMessage(
                    pending.NormalizedConversationLabel,
                    pending.NormalizedTranslatedText,
                    DateTimeOffset.UtcNow));
                pendingOutgoingDirectMessages.RemoveAt(i);
                break;
            }
        }

        if (completedResult != null)
        {
            translationCoordinator.RecordTranslationResult(completedResult);
            return true;
        }

        return false;
    }

    private void CleanupPendingOutgoingDirectMessages()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-15);
        pendingOutgoingDirectMessages.RemoveAll(pending => pending.CreatedAtUtc < cutoff);
    }

    private void CancelMostRecentPendingOutgoingDirectMessage()
    {
        lock (syncRoot)
        {
            CleanupPendingOutgoingDirectMessages();
            if (pendingOutgoingDirectMessages.Count == 0)
                return;

            pendingOutgoingDirectMessages.RemoveAt(pendingOutgoingDirectMessages.Count - 1);
        }
    }

    private bool ShouldSuppressConfirmedOutgoingEcho(string senderText, string messageText)
    {
        var normalizedSender = Normalize(senderText);
        var normalizedMessage = Normalize(messageText);

        lock (syncRoot)
        {
            CleanupConfirmedOutgoingDirectMessages();
            return confirmedOutgoingDirectMessages.Any(confirmed =>
                string.Equals(confirmed.NormalizedConversationLabel, normalizedSender, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(confirmed.NormalizedTranslatedText, normalizedMessage, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void CleanupConfirmedOutgoingDirectMessages()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-5);
        confirmedOutgoingDirectMessages.RemoveAll(confirmed => confirmed.CreatedAtUtc < cutoff);
    }

    private static bool LooksLikeDirectMessageSendError(string messageText)
        => DirectMessageErrorSnippets.Any(snippet => messageText.Contains(snippet, StringComparison.OrdinalIgnoreCase));

    private static string CaptureOriginalSeString(SeString message)
    {
        if (message.Payloads.All(payload => payload is TextPayload))
            return string.Empty;

        try
        {
            return Convert.ToBase64String(message.Encode());
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record PendingOutgoingDirectMessage(
        TranslationResult Result,
        string NormalizedConversationLabel,
        string NormalizedTranslatedText,
        DateTimeOffset CreatedAtUtc);

    private sealed record ConfirmedOutgoingDirectMessage(
        string NormalizedConversationLabel,
        string NormalizedTranslatedText,
        DateTimeOffset CreatedAtUtc);
}
