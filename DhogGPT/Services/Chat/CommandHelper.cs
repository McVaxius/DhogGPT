using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Component.GUI;
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
            case OutgoingChannel.PvPTeam:
                prefix = "/pvpteam ";
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

    public static unsafe bool SendEncodedSeString(byte[] bytes)
    {
        try
        {
            if (bytes.Length == 0)
            {
                Plugin.Log.Warning("[DhogGPT] Refusing to send an empty encoded SeString.");
                return false;
            }

            if (bytes.Length > 500)
            {
                Plugin.Log.Warning($"[DhogGPT] Refusing to send encoded SeString longer than 500 bytes ({bytes.Length}).");
                return false;
            }

            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error("[DhogGPT] UIModule was null, encoded SeString was not sent.");
                return false;
            }

            var utf8 = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8, nint.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DhogGPT] Failed to send encoded SeString: {ex.Message}");
            return false;
        }
    }

    public static unsafe bool TryCaptureCurrentPayloadDraft(out string plainText, out string originalSeStringBase64)
    {
        plainText = string.Empty;
        originalSeStringBase64 = string.Empty;

        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("ChatLog", 1);
            if (addonPtr == nint.Zero)
                return false;

            var addon = (AtkUnitBase*)addonPtr;
            var inputNode = (AtkComponentNode*)addon->GetNodeById(5);
            if (inputNode == null || inputNode->Component == null)
                return false;

            var textNode = inputNode->Component->UldManager.SearchNodeById(16)->GetAsAtkTextNode();
            if (textNode == null)
                return false;

            var seString = SeString.Parse(textNode->NodeText);
            if (!seString.Payloads.Any(payload => payload is ItemPayload or MapLinkPayload))
                return false;

            plainText = seString.TextValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(plainText))
                return false;

            originalSeStringBase64 = Convert.ToBase64String(seString.Encode());
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[DhogGPT] Failed to capture current chat-box payload draft.");
            return false;
        }
    }

    public static unsafe void ClearCurrentChatInput()
    {
        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("ChatLog", 1);
            if (addonPtr == nint.Zero)
                return;

            var addon = (AtkUnitBase*)addonPtr;
            var inputNode = (AtkComponentNode*)addon->GetNodeById(5);
            if (inputNode == null || inputNode->Component == null)
                return;

            var textNode = inputNode->Component->UldManager.SearchNodeById(16)->GetAsAtkTextNode();
            var inputComponent = (AtkComponentTextInput*)inputNode->Component;
            inputComponent->EvaluatedString.Clear();
            inputComponent->RawString.Clear();
            inputComponent->AvailableLines.Clear();
            inputComponent->HighlightedAutoTranslateOptionColorPrefix.Clear();
            inputComponent->HighlightedAutoTranslateOptionColorSuffix.Clear();
            textNode->NodeText.Clear();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[DhogGPT] Failed to clear the live game chat input after payload send.");
        }
    }
}
