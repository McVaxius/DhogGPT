namespace DhogGPT.Models;

public sealed class TranslationResult
{
    public TranslationRequest Request { get; init; } = new();
    public bool Success { get; init; }
    public bool FromCache { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public string DetectedSourceLanguage { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }

    public bool HasMeaningfulTranslation =>
        Success && !string.Equals(Normalize(Request.Text), Normalize(TranslatedText), StringComparison.OrdinalIgnoreCase);

    public static TranslationResult Succeeded(
        TranslationRequest request,
        string translatedText,
        string providerName,
        string endpoint,
        string detectedSourceLanguage,
        TimeSpan duration,
        bool fromCache = false)
    {
        return new TranslationResult
        {
            Request = request,
            Success = true,
            FromCache = fromCache,
            ProviderName = providerName,
            Endpoint = endpoint,
            TranslatedText = translatedText,
            DetectedSourceLanguage = detectedSourceLanguage,
            Duration = duration,
        };
    }

    public static TranslationResult Failed(
        TranslationRequest request,
        string providerName,
        string endpoint,
        string error,
        TimeSpan duration)
    {
        return new TranslationResult
        {
            Request = request,
            Success = false,
            ProviderName = providerName,
            Endpoint = endpoint,
            Error = error,
            Duration = duration,
        };
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
