using System.Text;
using Dalamud.Game.Text;

namespace DhogGPT.Services.Chat;

public static class ChatChannelMapper
{
    public const string DirectMessageComposerKey = "channel:DM";

    public static bool TryGetIncomingChannelLabel(Configuration configuration, XivChatType chatType, out string label)
    {
        switch (chatType)
        {
            case XivChatType.Party:
                label = "Party";
                return configuration.EnableParty;
            case XivChatType.Alliance:
                label = "Alliance";
                return configuration.EnableParty;
            case XivChatType.FreeCompany:
                label = "FC";
                return configuration.EnableFreeCompany;
            case XivChatType.Say:
                label = "Say";
                return configuration.EnableSay;
            case XivChatType.Shout:
                label = "Shout";
                return configuration.EnableShout;
            case XivChatType.Yell:
                label = "Yell";
                return configuration.EnableYell;
            case XivChatType.TellIncoming:
            case XivChatType.TellOutgoing:
                label = "DM";
                return configuration.EnableTell;
            case XivChatType.Ls1:
            case XivChatType.Ls2:
            case XivChatType.Ls3:
            case XivChatType.Ls4:
            case XivChatType.Ls5:
            case XivChatType.Ls6:
            case XivChatType.Ls7:
            case XivChatType.Ls8:
                label = $"LS{GetLinkshellSlot(chatType)}";
                return configuration.EnableLinkshells;
            case XivChatType.CrossLinkShell1:
            case XivChatType.CrossLinkShell2:
            case XivChatType.CrossLinkShell3:
            case XivChatType.CrossLinkShell4:
            case XivChatType.CrossLinkShell5:
            case XivChatType.CrossLinkShell6:
            case XivChatType.CrossLinkShell7:
            case XivChatType.CrossLinkShell8:
                label = $"CWLS{GetCrossWorldLinkshellSlot(chatType)}";
                return configuration.EnableCrossWorldLinkshells;
            default:
                label = string.Empty;
                return false;
        }
    }

    public static string GetOutgoingLabel(Configuration configuration)
    {
        return configuration.SelectedOutgoingChannel switch
        {
            OutgoingChannel.Say => "Say",
            OutgoingChannel.Party => "Party",
            OutgoingChannel.Alliance => "Alliance",
            OutgoingChannel.FreeCompany => "FC",
            OutgoingChannel.Linkshell => $"LS{Math.Clamp(configuration.LinkshellSlot, 1, 8)}",
            OutgoingChannel.CrossWorldLinkshell => $"CWLS{Math.Clamp(configuration.CrossWorldLinkshellSlot, 1, 8)}",
            OutgoingChannel.Shout => "Shout",
            OutgoingChannel.Yell => "Yell",
            OutgoingChannel.Tell => "DM",
            _ => "Unknown",
        };
    }

    public static (string Key, string Label) GetIncomingConversation(string channelLabel, string sender)
    {
        if (string.Equals(channelLabel, "DM", StringComparison.OrdinalIgnoreCase))
        {
            var label = NormalizeDirectMessageLabel(sender);
            return (GetDirectMessageConversationKey(label), label);
        }

        return ($"channel:{NormalizeConversationToken(channelLabel)}", channelLabel);
    }

    public static (string Key, string Label) GetOutgoingConversation(Configuration configuration)
    {
        var channelLabel = GetOutgoingLabel(configuration);
        if (configuration.SelectedOutgoingChannel == OutgoingChannel.Tell)
        {
            if (string.IsNullOrWhiteSpace(configuration.TellTarget))
                return (DirectMessageComposerKey, "DM");

            var label = NormalizeDirectMessageLabel(configuration.TellTarget);
            return (GetDirectMessageConversationKey(label), label);
        }

        return ($"channel:{NormalizeConversationToken(channelLabel)}", channelLabel);
    }

    public static string GetDirectMessageConversationKey(string value)
        => $"dm:{NormalizeDirectMessageToken(value)}";

    public static string NormalizeDirectMessageLabel(string value)
    {
        var cleaned = CleanDirectMessageIdentity(value);
        return string.IsNullOrWhiteSpace(cleaned) ? "DM" : cleaned;
    }

    private static int GetLinkshellSlot(XivChatType chatType)
    {
        return chatType switch
        {
            XivChatType.Ls1 => 1,
            XivChatType.Ls2 => 2,
            XivChatType.Ls3 => 3,
            XivChatType.Ls4 => 4,
            XivChatType.Ls5 => 5,
            XivChatType.Ls6 => 6,
            XivChatType.Ls7 => 7,
            XivChatType.Ls8 => 8,
            _ => 1,
        };
    }

    private static int GetCrossWorldLinkshellSlot(XivChatType chatType)
    {
        return chatType switch
        {
            XivChatType.CrossLinkShell1 => 1,
            XivChatType.CrossLinkShell2 => 2,
            XivChatType.CrossLinkShell3 => 3,
            XivChatType.CrossLinkShell4 => 4,
            XivChatType.CrossLinkShell5 => 5,
            XivChatType.CrossLinkShell6 => 6,
            XivChatType.CrossLinkShell7 => 7,
            XivChatType.CrossLinkShell8 => 8,
            _ => 1,
        };
    }

    private static string NormalizeConversationToken(string value)
    {
        return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private static string NormalizeDirectMessageToken(string value)
    {
        var label = NormalizeDirectMessageLabel(value);
        var atIndex = label.IndexOf('@');
        if (atIndex >= 0)
            label = label[..atIndex];

        return NormalizeConversationToken(label);
    }

    private static string CleanDirectMessageIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) ||
                character is ' ' or '\'' or '-' or '@')
            {
                builder.Append(character);
            }
        }

        return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
