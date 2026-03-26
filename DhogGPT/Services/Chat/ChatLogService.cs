using System.Text;
using System.Text.Json;
using DhogGPT.Models;
using DhogGPT.Services.Translation;

namespace DhogGPT.Services.Chat;

public sealed class ChatLogService : IDisposable
{
    private const int MaxInMemoryEntries = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object syncRoot = new();
    private readonly TranslationCoordinator translationCoordinator;
    private readonly List<TranslationHistoryItem> entries = [];

    private string? activeLogPath;

    public ChatLogService(TranslationCoordinator translationCoordinator)
    {
        this.translationCoordinator = translationCoordinator;
        this.translationCoordinator.TranslationCompleted += OnTranslationCompleted;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        translationCoordinator.TranslationCompleted -= OnTranslationCompleted;
        Plugin.Framework.Update -= OnFrameworkUpdate;
    }

    public IReadOnlyList<TranslationHistoryItem> GetEntriesSnapshot()
    {
        lock (syncRoot)
        {
            EnsureCurrentContextLoaded();
            return entries.ToArray();
        }
    }

    private void OnTranslationCompleted(TranslationResult result)
    {
        if (!result.Request.RecordInHistory)
            return;

        var entry = TranslationHistoryItem.FromResult(result);
        lock (syncRoot)
        {
            EnsureCurrentContextLoaded();
            entries.Insert(0, entry);
            if (entries.Count > MaxInMemoryEntries)
                entries.RemoveRange(MaxInMemoryEntries, entries.Count - MaxInMemoryEntries);

            if (activeLogPath == null)
                return;

            var directory = Path.GetDirectoryName(activeLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(
                activeLogPath,
                JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine,
                Encoding.UTF8);
        }
    }

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        lock (syncRoot)
        {
            EnsureCurrentContextLoaded();
        }
    }

    private void EnsureCurrentContextLoaded()
    {
        var logPath = BuildCurrentLogPath();
        if (logPath == null)
            return;

        if (activeLogPath != null && string.Equals(activeLogPath, logPath, StringComparison.OrdinalIgnoreCase))
            return;

        activeLogPath = logPath;
        entries.Clear();

        if (!File.Exists(activeLogPath))
            return;

        foreach (var line in File.ReadLines(activeLogPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = JsonSerializer.Deserialize<TranslationHistoryItem>(line, JsonOptions);
                if (entry != null)
                    entries.Add(entry);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[DhogGPT] Failed to parse chat log entry from {Path.GetFileName(activeLogPath)}: {ex.Message}");
            }
        }

        entries.Sort((left, right) => right.TimestampUtc.CompareTo(left.TimestampUtc));
        if (entries.Count > MaxInMemoryEntries)
            entries.RemoveRange(MaxInMemoryEntries, entries.Count - MaxInMemoryEntries);
    }

    private static string? BuildCurrentLogPath()
    {
        var contentId = Plugin.PlayerState.ContentId;
        var player = Plugin.ObjectTable.LocalPlayer;
        if (contentId == 0 || player == null)
            return null;

        var characterName = player.Name.TextValue;
        var worldName = player.HomeWorld.Value.Name.ToString();
        var accountId = contentId.ToString("X16");
        var characterKey = SanitizeFileSegment($"{characterName}@{worldName}");

        return Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName,
            "Data",
            "ChatLogs",
            accountId,
            $"{characterKey}.jsonl");
    }

    private static string SanitizeFileSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }
}
