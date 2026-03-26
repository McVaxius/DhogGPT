using System.Collections.Concurrent;

namespace DhogGPT.Services.Translation;

public sealed class TranslationCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> entries = new();
    private readonly TimeSpan ttl = TimeSpan.FromMinutes(10);
    private readonly int maxEntries = 500;

    public bool TryGet(string sourceLanguage, string targetLanguage, string text, out string translatedText, out string detectedSourceLanguage)
    {
        translatedText = string.Empty;
        detectedSourceLanguage = string.Empty;

        var key = BuildKey(sourceLanguage, targetLanguage, text);
        if (!entries.TryGetValue(key, out var entry))
            return false;

        if (DateTimeOffset.UtcNow - entry.CreatedAtUtc > ttl)
        {
            entries.TryRemove(key, out _);
            return false;
        }

        translatedText = entry.TranslatedText;
        detectedSourceLanguage = entry.DetectedSourceLanguage;
        return true;
    }

    public void Store(string sourceLanguage, string targetLanguage, string text, string translatedText, string detectedSourceLanguage)
    {
        var key = BuildKey(sourceLanguage, targetLanguage, text);
        entries[key] = new CacheEntry(translatedText, detectedSourceLanguage, DateTimeOffset.UtcNow);

        if (entries.Count > maxEntries)
            TrimExpired();
    }

    private void TrimExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            if (now - entry.Value.CreatedAtUtc > ttl || entries.Count > maxEntries)
                entries.TryRemove(entry.Key, out _);
        }
    }

    private static string BuildKey(string sourceLanguage, string targetLanguage, string text)
    {
        return $"{sourceLanguage.Trim().ToLowerInvariant()}::{targetLanguage.Trim().ToLowerInvariant()}::{text.Trim()}";
    }

    private readonly record struct CacheEntry(string TranslatedText, string DetectedSourceLanguage, DateTimeOffset CreatedAtUtc);
}
