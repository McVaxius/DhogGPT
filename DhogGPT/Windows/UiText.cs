using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace DhogGPT.Windows;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the DhogGPT UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文"),
        ("vi","Tiếng Việt"),("pt-BR","Português (Brasil)"),("id","Bahasa Indonesia"),("pl","Polski"),("tr","Türkçe"),("hi","हिन्दी")];
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    private readonly ResourceManager englishManager;
    private readonly Dictionary<string,string> labels;
    private readonly string[] concatenatedPrefixes;
    internal ResourceSet Resources { get; }
    internal IReadOnlyList<string> RequiredText { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    private readonly (Regex Pattern, string Key, string Prefix, int ArgumentCount)[] messageTemplates;
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("DhogGPT.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        englishManager=new ResourceManager("DhogGPT.Localization.Strings_en",typeof(UiText).Assembly);
        var english=englishManager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException("en");
        RequiredText=Values(Resources).Concat(Values(english)).Concat(Languages.Select(l=>l.Name)).Distinct().ToArray();
        labels=english.Cast<DictionaryEntry>().ToDictionary(entry=>(string)entry.Value!,entry=>(string)entry.Key,StringComparer.OrdinalIgnoreCase);
        var selectedKeys=Resources.Cast<DictionaryEntry>().Select(entry=>(string)entry.Key).ToHashSet(StringComparer.Ordinal);
        if (!selectedKeys.SetEquals(labels.Values) ||
            labels.Values.Any(key=>string.IsNullOrWhiteSpace(Resources.GetString(key,false)) && !string.IsNullOrWhiteSpace(english.GetString(key,false))))
            throw new MissingManifestResourceException("Incomplete DhogGPT UI translations for "+Language);
        concatenatedPrefixes=labels.Keys.Where(key=>key.EndsWith(' ') || key.EndsWith('.')).OrderByDescending(key=>key.Length).ToArray();
        this.pushFont=pushFont;
        // Service messages remain English in logs; only their UI copies are localized.
        var parameter = new Regex(@"\{(\d+)(?::([^}]+))?\}");
        messageTemplates = labels.Keys
            // Formatting-only entries must not consume arbitrary statuses before their composed parts.
            .Where(key => parameter.IsMatch(key) && key != "{0}")
            .OrderBy(key=>parameter.Match(key).Index==0?1:0).ThenByDescending(key=>key.Length).Select(key =>
            {
                var argumentCount = 0;
                var pattern = "^";
                var offset = 0;
                foreach (Match hole in parameter.Matches(key))
                {
                    var index = int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture);
                    pattern += Regex.Escape(key[offset..hole.Index]) + $"(?<arg{index}>.*?)";
                    argumentCount = Math.Max(argumentCount, index + 1);
                    offset = hole.Index + hole.Length;
                }
                pattern += Regex.Escape(key[offset..]) + "$";
                return (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)), key, key[..parameter.Match(key).Index], argumentCount);
            }).ToArray();
    }
    internal static string T(string english)
    {
        if (Current.labels.TryGetValue(english,out var resourceKey)) return Current.Resources.GetString(resourceKey,false)!;
        return english;
    }
    internal static string Status(string english)
    {
        if (Current.labels.TryGetValue(english,out var resourceKey)) return Current.Resources.GetString(resourceKey,false)!;
        // Provider failures join independent endpoint details; translate each detail before template captures.
        if (english.Contains(" | ",StringComparison.Ordinal)) return string.Join(" | ",english.Split(" | ",StringSplitOptions.None).Select(Status));
        foreach (var template in Current.messageTemplates)
        {
            if (!english.StartsWith(template.Prefix,StringComparison.Ordinal)) continue;
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;
            // Captures are already formatted service values; keep empty arguments and identifiers exact.
            var args = Enumerable.Range(0, template.ArgumentCount).Select(index =>
            {
                var value = match.Groups[$"arg{index}"].Value;
                return TranslateArgument(template.Key,index,value);
            }).ToArray();
            return string.Format(Current.Culture, Current.Resources.GetString(Current.labels[template.Key], false)!, args);
        }
        foreach (var prefix in Current.concatenatedPrefixes)
            if (english.Length>prefix.Length && english.StartsWith(prefix,StringComparison.Ordinal))
                return Current.Resources.GetString(Current.labels[prefix],false)!+" "+Status(english[prefix.Length..]);
        return english; // External names, command tokens and raw runtime data retain their original values.
    }
    private static string TranslateArgument(string format,int index,string value)
    {
        // Localize only consumer-authored status slots. Player names, endpoints and translated chat stay raw.
        return format is "Translation failed: {0}" or "Last error: {0}" ? Status(value) : value;
    }
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),
        args.Select((value,index)=>value is Enum e?T(e.ToString()):value is string s?TranslateArgument(english,index,s):value).ToArray());
    internal static string F(FormattableString text) => F(text.Format,text.GetArguments());
    internal static IDisposable Font(UiFontRole role) => Current.pushFont(role);
    internal Scope Enter() => new(this);
    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous=current; current=value; }
        public void Dispose() => current=previous;
    }
    internal static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("g",Current.Culture) ?? T("Never");
    internal ushort[] GlyphRanges(bool includeChat = false)
    {
        var chars=RequiredText.SelectMany(text=>MaterialText.NativeGlyphText(text).EnumerateRunes())
            .Where(rune=>rune.Value<=ushort.MaxValue && !System.Text.Rune.IsControl(rune)).Select(rune=>(char)rune.Value)
            .Concat(Enumerable.Range(0x20,0x024F-0x20+1).Select(i=>(char)i))
            .Concat(Enumerable.Range(0x0400,0x052F-0x0400+1).Select(i=>(char)i)).Concat("—")
            .Concat(includeChat ? Enumerable.Range(0x3000,0x9FFF-0x3000+1).Concat(Enumerable.Range(0xAC00,0xD7A3-0xAC00+1))
                .Concat(Enumerable.Range(0xFF00,0xFFEF-0xFF00+1)).Select(i=>(char)i) : Enumerable.Empty<char>())
            .Distinct().Order().ToArray();
        var result=new List<ushort>();
        for(var index=0;index<chars.Length;index++)
        {
            var first=chars[index]; var last=first;
            while(index+1<chars.Length && chars[index+1]==last+1) last=chars[++index];
            result.Add(first); result.Add(last);
        }
        result.Add(0); return result.ToArray();
    }
    internal static IEnumerable<string> Values(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e=>(string)e.Value!);
    public void Dispose() { manager.ReleaseAllResources();englishManager.ReleaseAllResources(); }
}
