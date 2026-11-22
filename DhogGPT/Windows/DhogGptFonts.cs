using System;
using System.Collections.Generic;
using System.Linq;
using AethertekUI;
using System.Text;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace DhogGPT.Windows;

internal sealed class DhogGptFonts : IDisposable
{
    private readonly IFontHandle[] handles;
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal DhogGptFonts(IFontAtlas atlas, ushort[] ranges, ushort[] chatRanges, string language, string assetRoot)
    {
        // Chat and composer text can use any supported CJK locale, independently of the UI language.
        handles = DhogGptPresentation.FontSizes.Select((_, index) => MaterialFontAtlas.CreateFont(atlas,
            DhogGptPresentation.FontWeights[index], DhogGptPresentation.AtlasHeight((UiFontRole)index),
            index == (int)UiFontRole.Body ? chatRanges : ranges, language, assetRoot)).ToArray();
        foreach(var handle in handles) handle.ImFontChanged+=FontChanged;
    }
    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => handles.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => handles.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        for(var index=0; index<handles.Length; index++)
        {
            using var font=handles[index].Lock();
            foreach(var text in strings)
                foreach(var rune in MaterialText.NativeGlyphText(text).EnumerateRunes().Where(rune=>!Rune.IsControl(rune)))
                    if(rune.Value>ushort.MaxValue || ImGui.FindGlyphNoFallback(font.ImFont,(ushort)rune.Value).Handle==null)
                        throw new InvalidOperationException("Required UI glyph missing: U+"+rune.Value.ToString("X4")+" in "+(UiFontRole)index);
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle=handles[(int)role];
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("DhogGPT fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose() { foreach(var handle in handles) { handle.ImFontChanged-=FontChanged;handle.Dispose(); } }
}
