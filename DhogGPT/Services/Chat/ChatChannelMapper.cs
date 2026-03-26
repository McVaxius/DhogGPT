using Dalamud.Game.Text;

namespace DhogGPT.Services.Chat;

public static class ChatChannelMapper
{
    public static bool TryGetIncomingChannelLabel(Configuration configuration, XivChatType chatType, out string label)
    {
        switch (chatType)
        {
            case XivChatType.Party:
                label = "Party";
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
            OutgoingChannel.FreeCompany => "FC",
            OutgoingChannel.Linkshell => $"LS{Math.Clamp(configuration.LinkshellSlot, 1, 8)}",
            OutgoingChannel.CrossWorldLinkshell => $"CWLS{Math.Clamp(configuration.CrossWorldLinkshellSlot, 1, 8)}",
            OutgoingChannel.Shout => "Shout",
            OutgoingChannel.Yell => "Yell",
            OutgoingChannel.Tell => "DM",
            _ => "Unknown",
        };
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
}
