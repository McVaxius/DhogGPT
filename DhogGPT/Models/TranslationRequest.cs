namespace DhogGPT.Models;

public sealed class TranslationRequest
{
    public string Text { get; init; } = string.Empty;
    public string OriginalSeStringBase64 { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "en";
    public bool IsInbound { get; init; }
    public string Sender { get; init; } = string.Empty;
    public string ChannelLabel { get; init; } = string.Empty;
    public string ConversationKey { get; init; } = string.Empty;
    public string ConversationLabel { get; init; } = string.Empty;
    public bool RecordInHistory { get; init; } = true;
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
