using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.Evaluator;
using DhogGPT.Models;

namespace DhogGPT.Services.Chat;

public sealed class SupplementalLogChannelService : IDisposable
{
    private static readonly HashSet<uint> IgnoredLogKinds =
    [
        (uint)XivChatType.Debug,
        (uint)XivChatType.Say,
        (uint)XivChatType.Shout,
        (uint)XivChatType.TellOutgoing,
        (uint)XivChatType.TellIncoming,
        (uint)XivChatType.Party,
        (uint)XivChatType.Alliance,
        (uint)XivChatType.Ls1,
        (uint)XivChatType.Ls2,
        (uint)XivChatType.Ls3,
        (uint)XivChatType.Ls4,
        (uint)XivChatType.Ls5,
        (uint)XivChatType.Ls6,
        (uint)XivChatType.Ls7,
        (uint)XivChatType.Ls8,
        (uint)XivChatType.FreeCompany,
        (uint)XivChatType.NoviceNetwork,
        (uint)XivChatType.CustomEmote,
        (uint)XivChatType.StandardEmote,
        (uint)XivChatType.Yell,
        (uint)XivChatType.CrossParty,
        (uint)XivChatType.PvPTeam,
        (uint)XivChatType.CrossLinkShell1,
        (uint)XivChatType.CrossLinkShell2,
        (uint)XivChatType.CrossLinkShell3,
        (uint)XivChatType.CrossLinkShell4,
        (uint)XivChatType.CrossLinkShell5,
        (uint)XivChatType.CrossLinkShell6,
        (uint)XivChatType.CrossLinkShell7,
        (uint)XivChatType.CrossLinkShell8,
        (uint)XivChatType.Echo,
        (uint)XivChatType.NPCDialogue,
        (uint)XivChatType.NPCDialogueAnnouncements,
    ];

    private static readonly string[] CombatKeywords =
    [
        "attacks",
        "casts",
        "critical hit",
        "damage",
        "defeated",
        "evades",
        "gains the effect",
        "healing",
        "hits",
        "interrupt",
        "misses",
        "parries",
        "recovers",
        "suffers",
        "uses",
    ];

    private readonly ChatLogService chatLogService;

    public SupplementalLogChannelService(ChatLogService chatLogService)
    {
        this.chatLogService = chatLogService;
        Plugin.ChatGui.LogMessage += OnLogMessage;
    }

    public void Dispose()
    {
        Plugin.ChatGui.LogMessage -= OnLogMessage;
    }

    private void OnLogMessage(ILogMessage message)
    {
        if (message.IsHandled)
            return;

        uint logKindId;
        try
        {
            logKindId = message.GameData.Value.LogKind.RowId;
        }
        catch
        {
            return;
        }

        if (IgnoredLogKinds.Contains(logKindId))
            return;

        var preview = EvaluateLogMessagePreview(message);
        if (string.IsNullOrWhiteSpace(preview))
            return;

        var channelLabel = ClassifyChannel(message, preview);
        var sourceName = NormalizeText(message.SourceEntity?.Name.ExtractText() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sourceName))
            sourceName = "System";

        chatLogService.AddTransientEntry(new TranslationHistoryItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            IsInbound = true,
            Success = true,
            ChannelLabel = channelLabel,
            Sender = sourceName,
            ConversationKey = $"channel:{channelLabel.ToUpperInvariant()}",
            ConversationLabel = channelLabel,
            OriginalText = preview,
            TranslatedText = string.Empty,
        });
    }

    private static string ClassifyChannel(ILogMessage message, string preview)
    {
        var lowered = preview.ToLowerInvariant();
        var hasCombatEntities = message.SourceEntity != null || message.TargetEntity != null;
        if (hasCombatEntities || CombatKeywords.Any(lowered.Contains))
            return "Combat";

        return "Progress";
    }

    private static string EvaluateLogMessagePreview(ILogMessage message)
    {
        try
        {
            var parameters = message.Parameters.Count == 0
                ? Array.Empty<SeStringParameter>()
                : message.Parameters.ToArray();
            var preview = Plugin.SeStringEvaluator
                .EvaluateFromLogMessage(message.LogMessageId, parameters, Plugin.ClientState.ClientLanguage)
                .ExtractText();
            return NormalizeText(preview);
        }
        catch
        {
            try
            {
                return NormalizeText(message.GameData.Value.Text.ExtractText());
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static string NormalizeText(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
