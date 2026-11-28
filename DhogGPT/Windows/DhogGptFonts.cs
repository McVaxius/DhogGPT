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
    private readonly IFontAtlas atlas;
    private readonly Dictionary<UiFontRole, IFontHandle> handles = [];
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal DhogGptFonts(IFontAtlas atlas, ushort[] ranges, ushort[] chatRanges, string language, string assetRoot,
        bool chat)
    {
        this.atlas = atlas;
        UiFontRole[] activeRoles = [UiFontRole.Body, UiFontRole.BodyStrong, UiFontRole.Title, UiFontRole.Heading];
        var glyphCount = 0;
        for (var index = 0; index + 1 < ranges.Length; index += 2)
            glyphCount += ranges[index + 1] - ranges[index] + 1;
        var pageSize = chat ? 4096 : glyphCount > 1000 ? 2048 : 1024;
        atlas.BuildStepChange += MaterialFontAtlas.ReleaseRasterizedInputs;
        try
        {
            foreach (var role in activeRoles)
            {
                // Conversation text keeps multilingual CJK independently of the UI language.
                var handle = MaterialFontAtlas.CreateFont(atlas, DhogGptPresentation.FontWeights[(int)role],
                    DhogGptPresentation.AtlasHeight(role), role == UiFontRole.Body ? chatRanges : ranges,
                    language, assetRoot, pageSize, includeHostLanguage: true);
                handles.Add(role, handle);
                handle.ImFontChanged += FontChanged;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IEnumerable<UiFontRole> Roles => handles.Keys;

    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => handles.Values.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => handles.Values.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        foreach (var (role, handle) in handles)
        {
            using var font = handle.Lock();
            foreach(var text in strings)
                foreach(var rune in MaterialText.NativeGlyphText(text).EnumerateRunes().Where(rune=>!Rune.IsControl(rune)))
                    if(rune.Value>ushort.MaxValue || ImGui.FindGlyphNoFallback(font.ImFont,(ushort)rune.Value).Handle==null)
                        throw new InvalidOperationException("Required UI glyph missing: U+"+rune.Value.ToString("X4")+" in "+role);
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle=handles.GetValueOrDefault(role, handles[UiFontRole.Body]);
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("DhogGPT fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose()
    {
        atlas.BuildStepChange -= MaterialFontAtlas.ReleaseRasterizedInputs;
        foreach (var handle in handles.Values)
        {
            handle.ImFontChanged -= FontChanged;
            handle.Dispose();
        }
        atlas.Dispose();
    }
}
