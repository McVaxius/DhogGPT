using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace DhogGPT.Services.Chat;

public readonly record struct ShellChannelDescriptor(
    string Key,
    int Slot,
    bool IsCrossWorld,
    string TechnicalLabel,
    string InGameLabel)
{
    public string GetDisplayLabel(Configuration configuration)
    {
        var key = Key;
        if (configuration.TechnicalShellConversationKeys.Any(existing =>
                string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
        {
            return TechnicalLabel;
        }

        return string.IsNullOrWhiteSpace(InGameLabel)
            ? TechnicalLabel
            : InGameLabel;
    }
}

public static class ShellChannelDisplayService
{
    private const int MaxShellSlots = 8;

    public static IReadOnlyList<ShellChannelDescriptor> GetLinkshellChannels()
        => ReadDescriptors(isCrossWorld: false);

    public static IReadOnlyList<ShellChannelDescriptor> GetCrossWorldLinkshellChannels()
        => ReadDescriptors(isCrossWorld: true);

    public static bool IsShellConversation(string conversationKey)
        => TryGetDescriptor(conversationKey, out _);

    public static bool TryGetDescriptor(string conversationKey, out ShellChannelDescriptor descriptor)
    {
        var isCrossWorld = conversationKey.StartsWith("channel:CWLS", StringComparison.OrdinalIgnoreCase);
        var prefix = isCrossWorld ? "channel:CWLS" : "channel:LS";
        if (!conversationKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = default;
            return false;
        }

        if (!int.TryParse(conversationKey[prefix.Length..], out var slot))
        {
            descriptor = default;
            return false;
        }

        slot = Math.Clamp(slot, 1, MaxShellSlots);
        var liveDescriptor = ReadDescriptors(isCrossWorld).FirstOrDefault(existing => existing.Slot == slot);
        if (!string.IsNullOrWhiteSpace(liveDescriptor.Key))
        {
            descriptor = liveDescriptor;
            return true;
        }

        var technicalLabel = isCrossWorld ? $"CWLS{slot}" : $"LS{slot}";
        descriptor = new ShellChannelDescriptor(conversationKey, slot, isCrossWorld, technicalLabel, string.Empty);
        return true;
    }

    public static string GetDisplayLabel(Configuration configuration, string conversationKey, string fallbackLabel)
    {
        if (!TryGetDescriptor(conversationKey, out var descriptor))
            return fallbackLabel;

        return descriptor.GetDisplayLabel(configuration);
    }

    public static bool UsesTechnicalLabel(Configuration configuration, string conversationKey)
        => configuration.TechnicalShellConversationKeys.Any(existing =>
            string.Equals(existing, conversationKey, StringComparison.OrdinalIgnoreCase));

    public static bool SetUseTechnicalLabel(Configuration configuration, string conversationKey, bool useTechnical)
    {
        if (!IsShellConversation(conversationKey))
            return false;

        configuration.TechnicalShellConversationKeys.RemoveAll(existing =>
            string.Equals(existing, conversationKey, StringComparison.OrdinalIgnoreCase));

        if (useTechnical)
            configuration.TechnicalShellConversationKeys.Add(conversationKey);

        return true;
    }

    private static List<ShellChannelDescriptor> ReadDescriptors(bool isCrossWorld)
    {
        var descriptors = new List<ShellChannelDescriptor>();
        try
        {
            unsafe
            {
                var infoModule = InfoModule.Instance();
                if (infoModule == null)
                    return descriptors;

                if (isCrossWorld)
                {
                    var proxy = (InfoProxyCrossWorldLinkshell*)infoModule->GetInfoProxyById(InfoProxyId.CrossWorldLinkshell);
                    if (proxy == null)
                        return descriptors;

                    for (uint slot = 0; slot < MaxShellSlots; slot++)
                    {
                        var technicalLabel = $"CWLS{slot + 1}";
                        var inGameLabel = Utf8StringToString(proxy->GetCrossworldLinkshellName(slot));
                        if (string.IsNullOrWhiteSpace(inGameLabel))
                            continue;

                        descriptors.Add(new ShellChannelDescriptor(
                            $"channel:{technicalLabel}",
                            (int)slot + 1,
                            true,
                            technicalLabel,
                            inGameLabel));
                    }

                    return descriptors;
                }

                var linkshellProxy = (InfoProxyLinkshell*)infoModule->GetInfoProxyById(InfoProxyId.Linkshell);
                if (linkshellProxy == null)
                    return descriptors;

                for (uint slot = 0; slot < MaxShellSlots; slot++)
                {
                    var info = linkshellProxy->GetLinkshellInfo(slot);
                    if (info == null || info->Id == 0)
                        continue;

                    var technicalLabel = $"LS{slot + 1}";
                    var inGameLabel = CStringToString(linkshellProxy->GetLinkshellName(info->Id));
                    descriptors.Add(new ShellChannelDescriptor(
                        $"channel:{technicalLabel}",
                        (int)slot + 1,
                        false,
                        technicalLabel,
                        inGameLabel));
                }
            }
        }
        catch
        {
            return descriptors;
        }

        return descriptors;
    }

    private static unsafe string Utf8StringToString(Utf8String* value)
    {
        if (value == null)
            return string.Empty;

        try
        {
            return value->ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CStringToString(InteropGenerator.Runtime.CStringPointer value)
    {
        try
        {
            return value.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
