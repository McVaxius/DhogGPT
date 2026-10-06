using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace DhogGPT.Windows;

internal enum UiFontRole { Body, BodyStrong, Title, Caption, Small, Heading, Action }

internal static class DhogGptPresentation
{
    // Measured DhogGPT-review-v2, compact-review-v1 and ultra-compact-v2 content geometry.
    internal const uint ReferenceAccent = 0x5551FF;
    internal static bool Compact { get; set; }
    internal static float Gap => Compact ? 8 : 14;
    internal static float ActionHeight => Compact ? 42 : 46;
    internal static float UltraFieldHeight => Compact ? 44 : 52;
    internal static float UltraTabHeight => Compact ? 40 : 46;
    internal static MaterialTheme UltraTheme { get; private set; } = null!;
    internal static readonly float[] FontSizes = [12,12,24,10,9,15,12];
    internal static readonly string[] FontFiles = ["segoeui.ttf","seguisb.ttf","segoeuib.ttf","segoeui.ttf","segoeui.ttf","seguisb.ttf","seguisb.ttf"];
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role]*4/3;
    internal readonly ref struct TextScale
    {
        private readonly float previous;
        internal TextScale(float multiplier)
        {
            previous = ImGuiP.GetCurrentWindow().FontWindowScale;
            ImGui.SetWindowFontScale(previous * multiplier);
        }
        public void Dispose() => ImGui.SetWindowFontScale(previous);
    }
    internal static Vector4 Rgb(uint rgb) => new(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1);
    internal static readonly Vector4 Success=Rgb(0x27D447), Failure=Rgb(0xF73865);
    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        Vector3 Triplet(Vector4 color) => new(color.X,color.Y,color.Z);
        var reference=MaterialColor.LabToLch(MaterialColor.SrgbToOklab(Triplet(Rgb(ReferenceAccent))));
        var selected=Rgb(accent);
        var seed=MaterialColor.LabToLch(MaterialColor.SrgbToOklab(Triplet(selected)));
        Vector4 Relative(uint rgb)
        {
            var color=Rgb(rgb);
            if(accent==ReferenceAccent)return color;
            var lch=MaterialColor.LabToLch(MaterialColor.SrgbToOklab(Triplet(color)));
            return new(MaterialColor.GamutMap(lch.X,seed.Y<.001f?0:lch.Y*seed.Y/reference.Y,lch.Z+(seed.Y<.001f?0:seed.Z-reference.Z)),1);
        }
        var background=Relative(0x111E2A);var text=Relative(0xF3F5FA);var primary=Relative(ReferenceAccent);
        var palette=new OklchPaletteGenerator().Generate(Triplet(selected));
        var colors=new MaterialColorScheme(palette)
        {
            Background=background,OnBackground=text,Surface=Relative(0x152633),OnSurface=text,
            SurfaceContainerLowest=Relative(0x17222E),SurfaceContainerLow=Relative(0x152430),
            SurfaceContainer=Relative(0x1B2938),SurfaceContainerHigh=Relative(0x1A2631),SurfaceContainerHighest=Relative(0x1E2A38),
            SurfaceVariant=Relative(0x2C3C4C),OnSurfaceVariant=Relative(0xB8C9E4),
            Outline=Relative(0x536881),OutlineVariant=Relative(0x2C4355),
            Primary=primary,OnPrimary=MaterialColor.Contrast(primary,background)>=MaterialColor.Contrast(primary,text)?background:text,
            PrimaryContainer=Relative(0x282846),OnPrimaryContainer=text,
            Secondary=Relative(0xB5B2F3),OnSecondary=background,SecondaryContainer=Relative(0x1B2B3B),OnSecondaryContainer=text,
            Tertiary=Relative(0xACCEE9),OnTertiary=background,TertiaryContainer=Relative(0x273D50),OnTertiaryContainer=text,
            InverseSurface=text,InverseOnSurface=background,InversePrimary=Relative(0xAAA7FF),
        };
        // Both surface palettes follow the existing accent cache; no font or preference changes.
        var ultraBackground=Relative(0x1B1E23);var ultraText=Relative(0xF3F4F6);
        var ultraPrimary=Relative(0x609BEC);
        UltraTheme=new(new MaterialColorScheme(palette)
        {
            Background=ultraBackground,OnBackground=ultraText,Surface=Relative(0x23262B),OnSurface=ultraText,
            SurfaceContainerLowest=ultraBackground,SurfaceContainerLow=Relative(0x23262B),
            SurfaceContainer=ultraBackground,SurfaceContainerHigh=Relative(0x292C32),SurfaceContainerHighest=Relative(0x2C2F34),
            SurfaceVariant=Relative(0x353A41),OnSurfaceVariant=Relative(0x979DA7),
            Outline=Relative(0x656A73),OutlineVariant=Relative(0x4B4F55),
            Primary=ultraPrimary,OnPrimary=ultraBackground,PrimaryContainer=Relative(0x23262B),OnPrimaryContainer=ultraText,
            Secondary=Relative(0xB5BAC3),OnSecondary=ultraBackground,SecondaryContainer=Relative(0x2C2F34),OnSecondaryContainer=ultraText,
            Tertiary=Relative(0xB5BAC3),OnTertiary=ultraBackground,TertiaryContainer=Relative(0x353A41),OnTertiaryContainer=ultraText,
            InverseSurface=ultraText,InverseOnSurface=ultraBackground,InversePrimary=ultraPrimary,
        });
        return new(colors);
    }
    internal static void SameLineIfFits(float width)
    {
        var right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
        var lastRight=Math.Max(ImGui.GetItemRectMax().X,ImGuiP.GetCurrentWindow().DC.CursorPosPrevLine.X);
        if(right-lastRight>=width+ImGui.GetStyle().ItemSpacing.X)ImGui.SameLine();
    }
    internal static void Brand(bool ultraCompact)
    {
        var size=(ultraCompact?28:Compact?38:48)*MaterialTheme.Metrics.Scale;
        var min=ImGui.GetCursorScreenPos();var color=MaterialTheme.Current.Colors.Primary;
        var dl=ImGui.GetWindowDrawList();
        dl.AddRectFilled(min+new Vector2(size*.2f,size*.15f),min+new Vector2(size,size*.82f),MaterialCanvas.Color(MaterialColor.Layer(color,MaterialTheme.Current.Colors.OnSurface,.12f)),size*.2f);
        dl.AddTriangleFilled(min+new Vector2(size*.62f,size*.68f),min+new Vector2(size*.95f,size*.97f),min+new Vector2(size*.95f,size*.7f),MaterialCanvas.Color(color));
        dl.AddRectFilled(min,min+new Vector2(size*.82f,size*.67f),MaterialCanvas.Color(color),size*.2f);
        dl.AddTriangleFilled(min+new Vector2(size*.12f,size*.5f),min+new Vector2(size*.12f,size*.88f),min+new Vector2(size*.38f,size*.55f),MaterialCanvas.Color(color));
        foreach(var x in new[]{.24f,.41f,.58f})dl.AddCircleFilled(min+new Vector2(size*x,size*.32f),size*.043f,MaterialCanvas.Color(MaterialTheme.Current.Colors.Background));
        ImGui.Dummy(new Vector2(size,size));ImGui.SameLine();ImGui.BeginGroup();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(ImGui.GetStyle().ItemSpacing.X,2*MaterialTheme.Metrics.Scale));
        using (var titleSize = new TextScale(Compact && !ultraCompact ? .83f : 1f))
            Text("DhogGPT",ultraCompact?UiFontRole.Heading:UiFontRole.Title,MaterialTheme.Current.Colors.OnSurface,Compact && !ultraCompact ? 28 : 32);
        if(!ultraCompact)
        {
            using var subtitleSize = new TextScale(Compact ? 1.8f : 1.4f);
            Text("Chat translation for FFXIV",UiFontRole.Body,MaterialTheme.Current.Colors.OnSurfaceVariant,18);
        }
        ImGui.PopStyleVar();
        ImGui.EndGroup();
    }
    internal static void Section(string label)
    {
        Text(label,UiFontRole.Heading,MaterialTheme.Current.Colors.OnSurface,20);
        ImGui.Separator();
    }
    internal static unsafe void Text(string label,UiFontRole role,Vector4 color,float minimumLogicalHeight)
    {
        using var scope=UiText.Font(role);
        var text=UiText.T(label);var font=ImGui.GetFont();var size=ImGui.GetFontSize();
        if(MaterialText.RequiresShaping(text))
        {
            var measured=MaterialText.Measure(text);
            var lineHeight=Math.Max(minimumLogicalHeight*MaterialTheme.Metrics.Scale,MathF.Ceiling(measured.Y));
            MaterialText.AddText(ImGui.GetWindowDrawList(),ImGui.GetCursorScreenPos()+new Vector2(0,(lineHeight-measured.Y)*.5f),MaterialCanvas.Color(color),text);
            ImGui.Dummy(new Vector2(measured.X,lineHeight));return;
        }
        var ratio=size/font.FontSize;var top=float.PositiveInfinity;var bottom=float.NegativeInfinity;
        foreach(var character in text)
        {
            var glyph=ImGui.FindGlyphNoFallback(font,character);
            if(glyph.Handle==null)throw new InvalidOperationException("Missing required UI glyph U+"+((int)character).ToString("X4"));
            if(glyph.Y1<=glyph.Y0)continue;
            top=Math.Min(top,glyph.Y0*ratio);bottom=Math.Max(bottom,glyph.Y1*ratio);
        }
        var inkHeight=float.IsFinite(top)?bottom-top:0;
        var height=Math.Max(minimumLogicalHeight*MaterialTheme.Metrics.Scale,MathF.Ceiling(inkHeight));
        var position=ImGui.GetCursorScreenPos()+new Vector2(0,(height-inkHeight)*.5f-(float.IsFinite(top)?top:0));
        ImGui.GetWindowDrawList().AddText(font,size,position,MaterialCanvas.Color(color),text);
        ImGui.Dummy(new Vector2(ImGui.CalcTextSize(text).X,height));
    }
    // Native-table cards follow the approved content height without resizing the user's window.
    internal ref struct Panel
    {
        private MaterialStyleScope style;
        internal bool Visible { get; }
        internal Panel(string id,MaterialElevation elevation=MaterialElevation.High,float? verticalPadding=null)
        {
            var root=ImGui.GetID("");
            var color=MaterialSurface.Color(Compact && elevation==MaterialElevation.High?MaterialElevation.Low:elevation);
            style=new();
            style.Color(ImGuiCol.TableRowBg,color);
            style.Color(ImGuiCol.TableRowBgAlt,color);
            style.Color(ImGuiCol.TableBorderStrong,MaterialTheme.Current.Colors.OutlineVariant);
            style.Color(ImGuiCol.TableBorderLight,MaterialTheme.Current.Colors.OutlineVariant);
            style.Style(ImGuiStyleVar.CellPadding,new Vector2(Compact?10:14,verticalPadding??(Compact?6:10))*MaterialTheme.Metrics.Scale);
            Visible=ImGui.BeginTable(id,1,ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersOuter|ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.NoSavedSettings);
            if(Visible) { ImGui.TableNextRow();ImGui.TableSetColumnIndex(0); }
            // Existing native control IDs are rooted outside the newly introduced presentation table.
            ImGuiP.PushOverrideID(root);
        }
        public void Dispose()
        {
            ImGui.PopID();if(Visible)ImGui.EndTable();style.Dispose();
        }
    }
    internal static bool PrimaryButton(string label,Vector2 size,MaterialIcon icon=MaterialIcon.None)
    {
        var c=MaterialTheme.Current.Colors;
        using var colors=new MaterialStyleScope();
        colors.Color(ImGuiCol.Button,c.Primary);colors.Color(ImGuiCol.ButtonHovered,MaterialColor.Layer(c.Primary,c.OnPrimary,.12f));
        colors.Color(ImGuiCol.ButtonActive,MaterialColor.Layer(c.Primary,c.OnPrimary,.22f));colors.Color(ImGuiCol.Text,c.OnPrimary);
        return UiGui.Button(label,size,icon:icon);
    }
}
