namespace DhogGPT.Models;

public sealed class TranslationRequest
{
    public string Text { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "en";
    public bool IsInbound { get; init; }
    public string Sender { get; init; } = string.Empty;
    public string ChannelLabel { get; init; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
