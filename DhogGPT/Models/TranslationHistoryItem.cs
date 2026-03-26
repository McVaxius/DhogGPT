namespace DhogGPT.Models;

public sealed class TranslationHistoryItem
{
    public DateTimeOffset TimestampUtc { get; init; }
    public bool IsInbound { get; init; }
    public bool Success { get; init; }
    public string ChannelLabel { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public string ConversationKey { get; init; } = string.Empty;
    public string ConversationLabel { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public string DetectedSourceLanguage { get; init; } = string.Empty;
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public bool FromCache { get; init; }

    public static TranslationHistoryItem FromResult(TranslationResult result)
    {
        return new TranslationHistoryItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            IsInbound = result.Request.IsInbound,
            Success = result.Success,
            ChannelLabel = result.Request.ChannelLabel,
            Sender = result.Request.Sender,
            ConversationKey = result.Request.ConversationKey,
            ConversationLabel = result.Request.ConversationLabel,
            SourceLanguage = result.Request.SourceLanguage,
            TargetLanguage = result.Request.TargetLanguage,
            DetectedSourceLanguage = result.DetectedSourceLanguage,
            OriginalText = result.Request.Text,
            TranslatedText = result.TranslatedText,
            ProviderName = result.ProviderName,
            Error = result.Error,
            FromCache = result.FromCache,
        };
    }
}
