using System;
using System.Collections.Generic;

namespace DhogGPT.Services;

public static class KrangleService
{
    private static readonly string[] ExerciseWords =
    {
        "Pushup", "Squat", "Lunge", "Plank", "Burpee", "Crunch", "Deadlift",
        "Curl", "Press", "Pullup", "Shrug", "Thrust", "Bridge", "Flutter",
        "Situp", "Sprawl", "Kata", "Kihon", "Kumite", "Ukemi", "Breakfall",
        "Sweep", "Roundhouse", "Jab", "Hook", "Cross", "Uppercut", "Parry",
        "Block", "Guard", "Stance", "Strike", "Punch", "Kick", "Elbow",
        "Knee", "Clinch", "Throw", "Grapple", "Armbar", "Choke", "Dodge",
        "Weave", "Slip", "Roll", "Feint", "Riposte", "Sprint", "Bench",
        "Clean", "Snatch", "Jerk", "Row", "Dip", "Step", "Jump", "Dash",
        "March", "Drill", "Crawl", "Climb", "Planche", "Muscle", "Lever",
        "Pistol", "Dragon", "Crane", "Tiger", "Mantis", "Viper", "Eagle",
    };

    private static readonly Dictionary<string, string> Cache = new();

    public static string KrangleName(string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName))
            return originalName;

        if (Cache.TryGetValue(originalName, out var cached))
            return cached;

        var atIndex = originalName.IndexOf('@');
        var characterPart = atIndex >= 0 ? originalName[..atIndex] : originalName;
        var serverPart = atIndex >= 0 ? originalName[(atIndex + 1)..] : string.Empty;

        var characterHash = GetStableHash(characterPart);
        var characterRng = new Random(characterHash);
        var first = ExerciseWords[characterRng.Next(ExerciseWords.Length)];
        var last = ExerciseWords[characterRng.Next(ExerciseWords.Length)];

        if (first.Length > 14)
            first = first[..14];

        if (last.Length > 14)
            last = last[..14];

        if (first.Length + 1 + last.Length > 22)
            last = last[..Math.Max(1, 22 - first.Length - 1)];

        var result = $"{first} {last}";
        if (!string.IsNullOrWhiteSpace(serverPart))
        {
            var serverHash = GetStableHash(serverPart);
            var serverRng = new Random(serverHash);
            result = $"{result}@{ExerciseWords[serverRng.Next(ExerciseWords.Length)]}";
        }

        Cache[originalName] = result;
        return result;
    }

    private static int GetStableHash(string input)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in input)
                hash = (hash * 31) + character;

            return hash;
        }
    }
}
