using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using DhogGPT.Models;

namespace DhogGPT.Services.Chat;

internal sealed class PreservedPayloadTranslationText
{
    private const string TokenPrefix = "__DHOGGPT_PRESERVE_";
    private static readonly PreservedPayloadTranslationText Empty = new(string.Empty, []);

    private readonly List<Replacement> replacements;

    private PreservedPayloadTranslationText(string preparedText, List<Replacement> replacements)
    {
        PreparedText = preparedText;
        this.replacements = replacements;
    }

    public string PreparedText { get; }

    public static PreservedPayloadTranslationText Prepare(TranslationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) ||
            string.IsNullOrWhiteSpace(request.OriginalSeStringBase64))
        {
            return Empty;
        }

        SeString seString;
        try
        {
            var bytes = Convert.FromBase64String(request.OriginalSeStringBase64);
            seString = SeString.Parse(bytes);
        }
        catch
        {
            return Empty;
        }

        var protectedSegments = ExtractProtectedSegments(seString);
        if (protectedSegments.Count == 0)
            return Empty;

        var preparedText = request.Text;
        var replacements = new List<Replacement>(protectedSegments.Count);
        var searchStart = 0;
        foreach (var segment in protectedSegments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var matchIndex = preparedText.IndexOf(segment, searchStart, StringComparison.Ordinal);
            if (matchIndex < 0)
                continue;

            var token = $"{TokenPrefix}{replacements.Count}__";
            preparedText = string.Concat(
                preparedText.AsSpan(0, matchIndex),
                token,
                preparedText.AsSpan(matchIndex + segment.Length));
            replacements.Add(new Replacement(token, segment));
            searchStart = matchIndex + token.Length;
        }

        return replacements.Count == 0
            ? Empty
            : new PreservedPayloadTranslationText(preparedText, replacements);
    }

    public TranslationRequest ApplyTo(TranslationRequest request)
        => string.Equals(PreparedText, request.Text, StringComparison.Ordinal)
            ? request
            : new TranslationRequest
            {
                Text = PreparedText,
                OriginalSeStringBase64 = request.OriginalSeStringBase64,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                IsInbound = request.IsInbound,
                Sender = request.Sender,
                ChannelLabel = request.ChannelLabel,
                ConversationKey = request.ConversationKey,
                ConversationLabel = request.ConversationLabel,
                RecordInHistory = request.RecordInHistory,
                RequestedAtUtc = request.RequestedAtUtc,
            };

    public string Restore(string translatedText)
    {
        if (replacements.Count == 0 || string.IsNullOrWhiteSpace(translatedText))
            return translatedText;

        var restored = translatedText;
        foreach (var replacement in replacements)
            restored = restored.Replace(replacement.Token, replacement.Value, StringComparison.Ordinal);

        return restored;
    }

    private static List<string> ExtractProtectedSegments(SeString seString)
    {
        var segments = new List<string>();
        var activeBuilder = new StringBuilder();
        var protectingPayloadText = false;

        void Flush()
        {
            if (activeBuilder.Length == 0)
                return;

            segments.Add(activeBuilder.ToString());
            activeBuilder.Clear();
        }

        foreach (var payload in seString.Payloads)
        {
            switch (payload)
            {
                case ItemPayload:
                case MapLinkPayload:
                    Flush();
                    protectingPayloadText = true;
                    continue;
                case RawPayload rawPayload when Equals(rawPayload, RawPayload.LinkTerminator):
                    Flush();
                    protectingPayloadText = false;
                    continue;
            }

            if (!protectingPayloadText || payload is not ITextProvider textProvider)
                continue;

            var text = textProvider.Text ?? string.Empty;
            var nullIndex = text.IndexOf('\0');
            if (nullIndex >= 0)
                text = text[..nullIndex];

            if (!string.IsNullOrEmpty(text))
                activeBuilder.Append(text);
        }

        Flush();
        return segments;
    }

    private sealed record Replacement(string Token, string Value);
}
