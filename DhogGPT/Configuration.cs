using Dalamud.Configuration;
using DhogGPT.Models;

namespace DhogGPT;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool PluginEnabled { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 1;
    public string DtrIconEnabled { get; set; } = "\uE044";
    public string DtrIconDisabled { get; set; } = "\uE04C";
    public bool HasSeenFirstUseGuide { get; set; }
    public bool TranslateIncoming { get; set; } = true;
    public bool SkipOwnMessages { get; set; } = true;
    public bool IncludeSenderName { get; set; } = true;
    public bool IncludeChannelLabel { get; set; } = true;
    public bool EnableDebugLogging { get; set; }
    public bool UseSimpleChatMode { get; set; } = true;
    public bool CompactSimpleChatMode { get; set; } = true;
    public bool KrangleChatNames { get; set; }
    public bool OpenMainWindowOnIncomingDirectMessage { get; set; } = true;
    public bool UseSuperCompactLanguageBar { get; set; } = true;
    public float WindowOpacity { get; set; } = 0.92f;
    public int CompactChatColorTheme { get; set; }
    public CompactChatCustomColors CompactChatCustomColors { get; set; } = new();

    public string IncomingSourceLanguage { get; set; } = "auto";
    public string IncomingTargetLanguage { get; set; } = "en";
    public string OutgoingSourceLanguage { get; set; } = "auto";
    public string OutgoingTargetLanguage { get; set; } = "en";

    public bool EnableParty { get; set; } = true;
    public bool EnableFreeCompany { get; set; } = true;
    public bool EnableLinkshells { get; set; } = true;
    public bool EnableCrossWorldLinkshells { get; set; } = true;
    public bool EnableSay { get; set; } = true;
    public bool EnableShout { get; set; } = true;
    public bool EnableYell { get; set; } = true;
    public bool EnableTell { get; set; } = true;

    public string ProviderEndpoints { get; set; } =
        "https://translate.argosopentech.com" + Environment.NewLine +
        "https://libretranslate.de";

    public int RequestTimeoutSeconds { get; set; } = 20;
    public int HistoryLimit { get; set; } = 30;

    public OutgoingChannel SelectedOutgoingChannel { get; set; } = OutgoingChannel.Party;
    public int LinkshellSlot { get; set; } = 1;
    public int CrossWorldLinkshellSlot { get; set; } = 1;
    public string TellTarget { get; set; } = string.Empty;
    public string OutgoingDraft { get; set; } = string.Empty;
    public List<string> PinnedDirectMessageTabs { get; set; } = [];
    public List<string> HiddenGeneralConversationKeys { get; set; } = [];

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
