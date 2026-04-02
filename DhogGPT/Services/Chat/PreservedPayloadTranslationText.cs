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
    public bool HasPayloadBlocks => replacements.Count > 0;

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
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            var matchIndex = preparedText.IndexOf(segment.Text, searchStart, StringComparison.Ordinal);
            if (matchIndex < 0)
                continue;

            var token = $"{TokenPrefix}{replacements.Count}__";
            preparedText = string.Concat(
                preparedText.AsSpan(0, matchIndex),
                token,
                preparedText.AsSpan(matchIndex + segment.Text.Length));
            replacements.Add(new Replacement(token, segment.Text, segment.Payloads));
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

    public bool TryBuildOutgoingSeStringBytes(string prefixText, string translatedText, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (replacements.Count == 0)
            return false;

        var payloads = new List<Payload>();
        if (!string.IsNullOrEmpty(prefixText))
            payloads.Add(new TextPayload(prefixText));

        var cursor = 0;
        foreach (var replacement in replacements)
        {
            var matchIndex = translatedText.IndexOf(replacement.Value, cursor, StringComparison.Ordinal);
            if (matchIndex < 0)
                return false;

            if (matchIndex > cursor)
                payloads.Add(new TextPayload(translatedText[cursor..matchIndex]));

            payloads.AddRange(replacement.Payloads);
            cursor = matchIndex + replacement.Value.Length;
        }

        if (cursor < translatedText.Length)
            payloads.Add(new TextPayload(translatedText[cursor..]));

        bytes = new SeString(payloads.ToArray()).Encode();
        return bytes.Length > 0;
    }

    private static List<ProtectedSegment> ExtractProtectedSegments(SeString seString)
    {
        var segments = new List<ProtectedSegment>();
        var activeBuilder = new StringBuilder();
        List<Payload>? activePayloads = null;
        var protectingPayloadText = false;

        void Flush()
        {
            if (activePayloads == null || activeBuilder.Length == 0)
                return;

            segments.Add(new ProtectedSegment(activeBuilder.ToString(), [.. activePayloads]));
            activeBuilder.Clear();
            activePayloads = null;
        }

        foreach (var payload in seString.Payloads)
        {
            switch (payload)
            {
                case ItemPayload:
                case MapLinkPayload:
                    Flush();
                    protectingPayloadText = true;
                    activePayloads = [payload];
                    continue;
                case RawPayload rawPayload when Equals(rawPayload, RawPayload.LinkTerminator):
                    activePayloads?.Add(payload);
                    Flush();
                    protectingPayloadText = false;
                    continue;
            }

            if (!protectingPayloadText)
                continue;

            activePayloads?.Add(payload);

            if (payload is ITextProvider textProvider)
            {
                var text = textProvider.Text ?? string.Empty;
                var nullIndex = text.IndexOf('\0');
                if (nullIndex >= 0)
                    text = text[..nullIndex];

                if (!string.IsNullOrEmpty(text))
                    activeBuilder.Append(text);
            }
        }

        Flush();
        return segments;
    }

    private sealed record ProtectedSegment(string Text, Payload[] Payloads);
    private sealed record Replacement(string Token, string Value, Payload[] Payloads);
}
