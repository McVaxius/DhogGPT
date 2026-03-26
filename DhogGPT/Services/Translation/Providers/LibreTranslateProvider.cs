using System.Net.Http.Json;
using System.Text.Json;
using DhogGPT.Models;

namespace DhogGPT.Services.Translation.Providers;

public sealed class LibreTranslateProvider : ITranslationProvider, IDisposable
{
    private const string GoogleEndpoint = "https://translate.googleapis.com/translate_a/single";

    private static readonly IReadOnlyList<string> DefaultEndpoints =
    [
        "https://translate.argosopentech.com/translate",
        "https://libretranslate.de/translate",
    ];

    private readonly Configuration configuration;
    private readonly HttpClient httpClient;

    public LibreTranslateProvider(Configuration configuration)
    {
        this.configuration = configuration;
        httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public string Name => "FreeWebTranslate";

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return TranslationResult.Failed(request, Name, string.Empty, "Message was empty.", TimeSpan.Zero);

        if (!request.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            request.SourceLanguage.Equals(request.TargetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return TranslationResult.Succeeded(
                request,
                request.Text,
                Name,
                "local-short-circuit",
                request.SourceLanguage,
                TimeSpan.Zero);
        }

        var googleResult = await TryGoogleTranslateAsync(request, cancellationToken).ConfigureAwait(false);
        if (googleResult != null)
            return googleResult;

        var endpoints = GetEndpoints();
        var errors = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var started = DateTimeOffset.UtcNow;

            try
            {
                using var response = await httpClient.PostAsJsonAsync(endpoint, new
                {
                    q = request.Text,
                    source = request.SourceLanguage,
                    target = request.TargetLanguage,
                    format = "text",
                }, cancellationToken).ConfigureAwait(false);

                var duration = DateTimeOffset.UtcNow - started;
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    errors.Add($"{endpoint}: HTTP {(int)response.StatusCode} {response.ReasonPhrase} {errorBody}".Trim());
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("translatedText", out var translatedElement))
                {
                    errors.Add($"{endpoint}: response did not contain translatedText.");
                    continue;
                }

                var translatedText = translatedElement.GetString() ?? string.Empty;
                var detectedLanguage = ReadDetectedLanguage(document.RootElement);

                return TranslationResult.Succeeded(
                    request,
                    translatedText,
                    "LibreTranslate",
                    endpoint,
                    detectedLanguage,
                    duration);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{endpoint}: {ex.Message}");
            }
        }

        return TranslationResult.Failed(
            request,
            "FreeWebTranslate",
            endpoints.LastOrDefault() ?? string.Empty,
            string.Join(" | ", errors),
            TimeSpan.Zero);
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private IReadOnlyList<string> GetEndpoints()
    {
        var configured = configuration.ProviderEndpoints
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeEndpoint)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configured.Length > 0 ? configured : DefaultEndpoints;
    }

    private async Task<TranslationResult?> TryGoogleTranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var started = DateTimeOffset.UtcNow;
            var query =
                $"client=gtx&sl={Uri.EscapeDataString(request.SourceLanguage)}&tl={Uri.EscapeDataString(request.TargetLanguage)}&dt=t&q={Uri.EscapeDataString(request.Text)}";

            using var response = await httpClient.GetAsync($"{GoogleEndpoint}?{query}", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                return null;

            var translatedText = ParseGoogleTranslatedText(document.RootElement);
            if (string.IsNullOrWhiteSpace(translatedText))
                return null;

            var detectedLanguage = document.RootElement.GetArrayLength() > 1 && document.RootElement[1].ValueKind == JsonValueKind.String
                ? document.RootElement[1].GetString() ?? string.Empty
                : string.Empty;

            return TranslationResult.Succeeded(
                request,
                translatedText,
                "GoogleTranslateWeb",
                GoogleEndpoint,
                detectedLanguage,
                DateTimeOffset.UtcNow - started);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeEndpoint(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        if (!normalized.EndsWith("/translate", StringComparison.OrdinalIgnoreCase))
            normalized += "/translate";

        return normalized;
    }

    private static string ReadDetectedLanguage(JsonElement root)
    {
        if (!root.TryGetProperty("detectedLanguage", out var detectedElement))
            return string.Empty;

        if (detectedElement.ValueKind == JsonValueKind.String)
            return detectedElement.GetString() ?? string.Empty;

        if (detectedElement.ValueKind == JsonValueKind.Object &&
            detectedElement.TryGetProperty("language", out var languageElement))
        {
            return languageElement.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string ParseGoogleTranslatedText(JsonElement root)
    {
        if (root[0].ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var segment in root[0].EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                continue;

            var piece = segment[0].GetString();
            if (!string.IsNullOrEmpty(piece))
                parts.Add(piece);
        }

        return string.Concat(parts);
    }
}
