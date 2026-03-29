using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DhogGPT.Services.Chat;

public static class CommandHelper
{
    public static bool TryBuildOutgoingCommand(Configuration configuration, string translatedText, out string command, out string error)
    {
        command = string.Empty;
        error = string.Empty;

        var trimmed = translatedText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "There is no translated text to send.";
            return false;
        }

        string prefix;
        switch (configuration.SelectedOutgoingChannel)
        {
            case OutgoingChannel.Safe:
                error = "Safe messages stay inside DhogGPT and are not sent to game chat.";
                return false;
            case OutgoingChannel.Echo:
                prefix = "/echo ";
                break;
            case OutgoingChannel.Say:
                prefix = "/s ";
                break;
            case OutgoingChannel.Party:
                prefix = "/p ";
                break;
            case OutgoingChannel.Alliance:
                prefix = "/a ";
                break;
            case OutgoingChannel.FreeCompany:
                prefix = "/fc ";
                break;
            case OutgoingChannel.Linkshell:
                prefix = $"/l{Math.Clamp(configuration.LinkshellSlot, 1, 8)} ";
                break;
            case OutgoingChannel.CrossWorldLinkshell:
                prefix = $"/cwl{Math.Clamp(configuration.CrossWorldLinkshellSlot, 1, 8)} ";
                break;
            case OutgoingChannel.Shout:
                prefix = "/sh ";
                break;
            case OutgoingChannel.Yell:
                prefix = "/y ";
                break;
            case OutgoingChannel.NoviceNetwork:
                prefix = "/beginner ";
                break;
            case OutgoingChannel.Tell:
                if (!ChatChannelMapper.TryNormalizeDirectMessageIdentity(configuration.TellTarget, out var normalizedIdentity, out var directMessageError))
                {
                    error = directMessageError;
                    return false;
                }

                prefix = $"/tell {normalizedIdentity} ";
                break;
            default:
                error = "Unsupported outgoing channel.";
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

    public static unsafe bool SendCommand(string command)
    {
        try
        {
            if (Plugin.CommandManager.ProcessCommand(command))
                return true;

            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error("[DhogGPT] UIModule was null, command was not sent.");
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8 = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8, nint.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DhogGPT] Failed to send command '{command}': {ex.Message}");
            return false;
        }
    }
}
