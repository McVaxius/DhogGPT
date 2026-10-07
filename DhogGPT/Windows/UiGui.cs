using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DhogGPT.Windows;

// Keep the ORIGINAL label passed to native ImGui. Translated ink is painted into the native item's measured bounds.
// This retains English-derived IDs, popup identity, selection, editing, focus and navigation behavior.
internal static class UiGui
{
    internal static void Text(string text) => MaterialText.Text(UiText.T(text));
    internal static void Text(FormattableString text) => MaterialText.Text(UiText.F(text));
    internal static void TextUnformatted(string text) => MaterialText.Text(UiText.T(text));
    internal static void TextWrapped(string text) => MaterialText.TextWrapped(UiText.T(text));
    internal static void TextDisabled(string text) { ImGui.PushTextWrapPos(0);MaterialText.TextDisabled(UiText.T(text));ImGui.PopTextWrapPos(); }
    internal static void TextColored(Vector4 color,string text) { ImGui.PushTextWrapPos(0);MaterialText.TextColored(color,UiText.T(text));ImGui.PopTextWrapPos(); }
    internal static void BulletText(string text) { ImGui.Bullet();MaterialText.TextWrapped(UiText.T(text)); }
    internal static void SetTooltip(string text) { ImGui.BeginTooltip();ImGui.PushTextWrapPos(Math.Min(560*MaterialTheme.Metrics.Scale,ImGui.GetMainViewport().WorkSize.X*.8f));MaterialText.Text(UiText.T(text));ImGui.PopTextWrapPos();ImGui.EndTooltip(); }
    private static string Visible(string label) => UiText.T(label.Split("##",2)[0]);
    private static void Ink(string label,Vector2 position,Vector2 min,Vector2 max)
    {
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(min,max,true);
        try { MaterialText.AddText(dl,position,ImGui.GetColorU32(ImGuiCol.Text),label); } finally { dl.PopClipRect(); }
    }
    internal static bool Button(string original,Vector2 size=default,string? display=null,MaterialIcon icon=MaterialIcon.None)
    {
        var translated=display ?? Visible(original);
        using var height=MaterialText.PushLineHeight(translated);
        var iconSize=icon==MaterialIcon.None?0:ImGui.GetFontSize();
        var iconGap=icon==MaterialIcon.None?0:ImGui.GetStyle().ItemInnerSpacing.X;
        var minimum=MaterialText.Measure(translated).X+iconSize+iconGap+ImGui.GetStyle().FramePadding.X*2;
        size.X=MaterialLayout.FitNextItemWidth(size.X,minimum);
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(original,size);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        var textSize=MaterialText.Measure(translated);
        var origin=min+new Vector2((max.X-min.X-textSize.X-iconSize-iconGap)*.5f,(max.Y-min.Y-textSize.Y)*.5f);
        if(icon!=MaterialIcon.None)
        {
            var drawing=ImGui.GetWindowDrawList();drawing.PushClipRect(min,max,true);
            MaterialIcons.Draw(icon,new Vector2(origin.X,(min.Y+max.Y-iconSize)*.5f),iconSize,ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            drawing.PopClipRect();
        }
        Ink(translated,origin+new Vector2(iconSize+iconGap,0),min,max);
        if (textSize.X+iconSize+iconGap>max.X-min.X-ImGui.GetStyle().FramePadding.X*2 && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
        return clicked;
    }
    internal static bool SmallButton(string label)
    { ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));var click=Button(label);ImGui.PopStyleVar();return click; }
    internal static bool Checkbox(string label,ref bool value,string? display=null)
    {
        var translated=UiText.T(display ?? label.Split("##",2)[0]);using var height=MaterialText.PushLineHeight(translated);var padding=ImGui.GetStyle().FramePadding;var gap=ImGui.GetStyle().ItemInnerSpacing;
        var original=label.Split("##",2)[0];
        var textSize=MaterialText.Measure(translated);
        var shaped=MaterialText.RequiresShaping(translated);
        var originalWidth=ImGui.CalcTextSize(original).X;
        var reservedWidth=shaped?MathF.Ceiling(textSize.X)+1:textSize.X;
        MaterialLayout.FitNextItemWidth(0,ImGui.GetFrameHeight()+(textSize.X>0?gap.X+(shaped?Math.Max(originalWidth,reservedWidth):textSize.X):0));
        var innerSpacing=gap.X+reservedWidth-originalWidth;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(shaped?Math.Max(0,innerSpacing):innerSpacing,gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.Checkbox(label,ref value);ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        var p=min+new Vector2(ImGui.GetFrameHeight()+gap.X,(max.Y-min.Y-textSize.Y)*.5f);
        if(original.Length==0 && textSize.X>0)
        {
            // A hidden native label retains its square hit target; reserve the separately painted caption.
            var window=ImGuiP.GetCurrentWindow();max.X=p.X+reservedWidth;
            window.DC.CursorMaxPos=new Vector2(Math.Max(window.DC.CursorMaxPos.X,max.X),window.DC.CursorMaxPos.Y);
            window.DC.CursorPosPrevLine=new Vector2(max.X,window.DC.CursorPosPrevLine.Y);
        }
        Ink(translated,p,min,max);
        return changed;
    }
    internal static bool RadioButton(string label,bool selected,string? display=null)
    {
        var translated=UiText.T(display ?? label.Split("##",2)[0]);using var height=MaterialText.PushLineHeight(translated);var original=label.Split("##",2)[0];var gap=ImGui.GetStyle().ItemInnerSpacing;
        MaterialLayout.FitNextItemWidth(0,ImGui.GetFrameHeight()+MaterialText.Measure(translated).X+gap.X);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(gap.X+MaterialText.Measure(translated).X-ImGui.CalcTextSize(original).X,gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.RadioButton(label,selected);ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var p=min+new Vector2(ImGui.GetFrameHeight()+gap.X,(ImGui.GetItemRectMax().Y-min.Y-MaterialText.Measure(translated).Y)*.5f);
        Ink(translated,p,min,new Vector2(p.X+MaterialText.Measure(translated).X,ImGui.GetItemRectMax().Y));return changed;
    }

    internal static bool CollapsingHeader(string label,ImGuiTreeNodeFlags flags=ImGuiTreeNodeFlags.None)
    {
        using var height=MaterialText.PushLineHeight(Visible(label));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.CollapsingHeader(label,flags);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();var padding=ImGui.GetStyle().FramePadding;
        MaterialIcons.Draw(open?MaterialIcon.ChevronDown:MaterialIcon.ArrowRight,min+padding,ImGui.GetFontSize(),MaterialTheme.Current.Colors.OnSurface,ImGui.GetStyle().Alpha);
        Ink(Visible(label),min+new Vector2(ImGui.GetFontSize()+padding.X*2,(max.Y-min.Y-MaterialText.Measure(Visible(label)).Y)*.5f),min,max);return open;
    }
    internal static bool TreeNode(string label)
    {
        using var height=MaterialText.PushLineHeight(Visible(label));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=MaterialText.RequiresShaping(Visible(label)) ? ImGui.TreeNodeEx(label,ImGuiTreeNodeFlags.FramePadding) : ImGui.TreeNode(label);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        MaterialIcons.Draw(open?MaterialIcon.ChevronDown:MaterialIcon.ArrowRight,min,ImGui.GetFontSize(),MaterialTheme.Current.Colors.OnSurface,ImGui.GetStyle().Alpha);
        var p=min+new Vector2(ImGui.GetFontSize()+ImGui.GetStyle().FramePadding.X*2,(max.Y-min.Y-MaterialText.Measure(Visible(label)).Y)*.5f);
        Ink(Visible(label),p,min,new Vector2(p.X+MaterialText.Measure(Visible(label)).X,max.Y));return open;
    }
    internal static bool BeginTabBar(string id, IReadOnlyList<string> captions, ImGuiTabBarFlags flags=ImGuiTabBarFlags.None)
    {
        using var height=MaterialText.PushLineHeight(captions.ToArray());
        return ImGui.BeginTabBar(id,flags);
    }
    private static MaterialStyleScope TabHeight()
    {
        var scope=new MaterialStyleScope();var bar=ImGui.GetCurrentContext().CurrentTabBar;
        if(!bar.IsNull && bar.FramePadding!=ImGui.GetStyle().FramePadding) scope.Style(ImGuiStyleVar.FramePadding,bar.FramePadding);
        return scope;
    }
    internal static bool BeginTabItem(string label,ImGuiTabItemFlags flags=ImGuiTabItemFlags.None)
    {
        using var height=TabHeight();
        var raw=label.Split("##",2)[0];
        var translated=Visible(label);
        ImGui.SetNextItemWidth(Math.Max(ImGui.CalcTextSize(raw).X,MaterialText.Measure(translated).X)+ImGui.GetStyle().FramePadding.X*2+12*MaterialTheme.Metrics.Scale);
        var drawing=ImGui.GetWindowDrawList();var first=drawing.VtxBuffer.Size;
        var open=ImGui.BeginTabItem(label,flags);
        try { TranslatedTabCaption(drawing,first,raw,translated,false); }
        catch { if(open)ImGui.EndTabItem();throw; }
        return open;
    }

    internal static bool BeginCombo(string label,string preview,ImGuiComboFlags flags=ImGuiComboFlags.None,bool translatePreview=true,bool showLabel=true)
    {
        var shown=translatePreview?UiText.T(preview):preview;
        using var height=MaterialText.PushLineHeight(shown,showLabel?Visible(label):"");
        // Native previews clip at their text origin; leave room for a negative left glyph bearing.
        var nativePreview=shown.Length==0?shown:" "+shown;
        var minimum=Math.Max(80*MaterialTheme.Metrics.Scale,MaterialText.Measure(nativePreview).X+ImGui.GetFrameHeight()+2*ImGui.GetStyle().FramePadding.X);
        var width=FitField(label,minimumPixels:minimum,showLabel:showLabel);var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();
        var clipped=showLabel?ClipFieldLabel(label,min,width,dl):true;
        if(!showLabel)dl.PushClipRect(min,min+new Vector2(width,ImGui.GetFrameHeight()),true);
        var shapedPreview=MaterialText.RequiresShaping(nativePreview) && (flags&ImGuiComboFlags.NoPreview)==0;
        var previewSize=MaterialText.Measure(nativePreview);var frameHeight=ImGui.GetFrameHeight();
        var previewPadding=ImGui.GetStyle().FramePadding;var previewFont=ImGui.GetFont();var previewFontSize=ImGui.GetFontSize();var previewColor=ImGui.GetColorU32(ImGuiCol.Text);
        bool open;
        try { open=ImGui.BeginCombo(label,shapedPreview?"":nativePreview,flags); } finally { if(clipped)dl.PopClipRect(); }
        try
        {
            if(shapedPreview)
            {
                var inset=(flags&ImGuiComboFlags.NoArrowButton)==0?frameHeight:previewPadding.X;
                dl.PushClipRect(min,min+new Vector2(Math.Max(previewPadding.X,width-inset),frameHeight),true);
                try { MaterialText.AddText(dl,previewFont,previewFontSize,min+new Vector2(previewPadding.X,(frameHeight-previewSize.Y)*.5f),previewColor,nativePreview); }
                finally { dl.PopClipRect(); }
            }
            if(showLabel)FieldLabel(label,min,width,dl,parent,previousMax);
            else HiddenFieldBounds(parent,previousMax,min,width);
        }
        catch { if(open)ImGui.EndCombo();throw; }
        if(!open && MaterialText.Measure(nativePreview).X>width-ImGui.GetFrameHeight() && ImGui.IsItemHovered()) MaterialText.SetTooltip(shown);
        return open;
    }
    internal static bool Selectable(string label,bool selected=false,ImGuiSelectableFlags flags=ImGuiSelectableFlags.None,Vector2 size=default)
    {
        if(MaterialText.RequiresShaping(Visible(label)))return MaterialText.Selectable(label,selected,flags,size,Visible(label));
        var origin=ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.Selectable(label,selected,flags,size);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();Ink(Visible(label),origin,min,max);
        if (MaterialText.Measure(Visible(label)).X>max.X-min.X && ImGui.IsItemHovered()) MaterialText.SetTooltip(Visible(label));return changed;
    }
    private static float FitField(string label,float? requestedPixels=null,float? minimumPixels=null,bool showLabel=true)
    {
        var requested=requestedPixels ?? ImGui.CalcItemWidth();
        var minimum=minimumPixels ?? 80*MaterialTheme.Metrics.Scale;
        var text=showLabel?Visible(label):string.Empty;
        var labelWidth=text.Length==0?0:MaterialText.Measure(text).X+ImGui.GetStyle().ItemInnerSpacing.X;
        var total=MaterialLayout.FitNextItemWidth(requested+labelWidth,minimum+labelWidth);
        var width=MathF.Ceiling(Math.Max(minimum,total-labelWidth));
        ImGui.SetNextItemWidth(width);return width;
    }
    private static void HiddenFieldBounds(ImGuiWindowPtr window,Vector2 previousMax,Vector2 min,float width)
    {
        window.DC.CursorMaxPos=new Vector2(Math.Max(previousMax.X,min.X+width),window.DC.CursorMaxPos.Y);
        window.DC.CursorPosPrevLine=new Vector2(min.X+width,window.DC.CursorPosPrevLine.Y);
    }
    private static bool ClipFieldLabel(string original,Vector2 min,float width,ImDrawListPtr drawing)
    {
        if(Visible(original)==original.Split("##",2)[0] && !MaterialText.RequiresShaping(Visible(original)))return false;
        // Clip only the native label ink; input content, editing and the original item ID remain native.
        // An opaque repaint cannot hide an English label over a transparent presentation child.
        var window=ImGui.GetWindowPos();
        drawing.PushClipRect(new Vector2(min.X,window.Y),new Vector2(min.X+width,window.Y+ImGui.GetWindowSize().Y),true);
        return true;
    }
    private static void FieldLabel(string original,Vector2 min,float width,ImDrawListPtr? drawing=null,ImGuiWindowPtr? layoutWindow=null,Vector2? previousMax=null)
    {
        var visible=original.Split("##",2)[0];var translated=UiText.T(visible);if (translated==visible && !MaterialText.RequiresShaping(translated)) return;
        var p=min+new Vector2(width+ImGui.GetStyle().ItemInnerSpacing.X,(ImGui.GetFrameHeight()-MaterialText.Measure(translated).Y)*.5f);
        var dl=drawing ?? ImGui.GetWindowDrawList();
        MaterialText.AddText(dl,p,ImGui.GetColorU32(ImGuiCol.Text),translated);
        // Replace the native label's layout width as well as its ink, retaining editing and IDs.
        var window=layoutWindow ?? ImGuiP.GetCurrentWindow();
        var right=p.X+MaterialText.Measure(translated).X;
        window.DC.CursorMaxPos=new Vector2(Math.Max((previousMax ?? window.DC.CursorMaxPos).X,right),window.DC.CursorMaxPos.Y);
        window.DC.CursorPosPrevLine=new Vector2(right,window.DC.CursorPosPrevLine.Y);
    }
    internal static bool InputText(string label,ref string value,int length,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None,bool showLabel=true)
    {
        using var height=MaterialText.PushLineHeight(value,showLabel?Visible(label):"");
        var width=FitField(label,showLabel:showLabel);var min=ImGui.GetCursorScreenPos();
        var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;
        var dl=ImGui.GetWindowDrawList();
        var clipped=showLabel?ClipFieldLabel(label,min,width,dl):true;
        if (!showLabel) dl.PushClipRect(min,min+new Vector2(width,ImGui.GetFrameHeight()),true);
        bool changed;try { changed=MaterialShapedInput.SingleLine(label,"",ref value,length,flags); } finally { if(clipped)dl.PopClipRect(); }
        if(showLabel)FieldLabel(label,min,width,dl,parent,previousMax);else HiddenFieldBounds(parent,previousMax,min,width);
        return changed;
    }
    internal static bool InputTextWithHint(string label,string hint,ref string value,int length,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { using var height=MaterialText.PushLineHeight(value,UiText.T(hint),Visible(label));var width=FitField(label);var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,min,width,dl);bool changed;try { changed=MaterialShapedInput.SingleLine(label,UiText.T(hint),ref value,length,flags); } finally { if(clipped)dl.PopClipRect(); }FieldLabel(label,min,width,dl,parent,previousMax);return changed; }
    internal static bool InputTextMultiline(string label,ref string value,int length,Vector2 size,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None,bool showLabel=true)
    {
        var requested=size.X<0?ImGui.GetContentRegionAvail().X+size.X:size.X>0?size.X:ImGui.CalcItemWidth();var width=FitField(label,requested,showLabel:showLabel);size.X=width;var min=ImGui.GetCursorScreenPos();var dl=ImGui.GetWindowDrawList();
        var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;
        var clipped=showLabel?ClipFieldLabel(label,min,width,dl):true;
        if(!showLabel)dl.PushClipRect(min,min+new Vector2(width,size.Y),true);
        bool changed;try { changed=MaterialShapedInput.Multiline(label,ref value,length,size,flags); } finally { if(clipped)dl.PopClipRect(); }if(showLabel)FieldLabel(label,min,width,dl,parent,previousMax);else HiddenFieldBounds(parent,previousMax,min,width);return changed;
    }
    internal static bool InputInt(string label,ref int value,int step=1,int fastStep=100,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { using var height=MaterialText.PushLineHeight(Visible(label));var width=FitField(label);var min=ImGui.GetCursorScreenPos();var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,min,width,dl);var changed=ImGui.InputInt(label,ref value,step,fastStep,"%d",flags);if(clipped)dl.PopClipRect();FieldLabel(label,min,width);return changed; }
    internal static bool InputFloat(string label,ref float value,float step=0,float fastStep=0,string format="%.3f",ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { using var height=MaterialText.PushLineHeight(Visible(label));var width=FitField(label);var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,min,width,dl);var changed=ImGui.InputFloat(label,ref value,step,fastStep,format,flags);if(clipped)dl.PopClipRect();FieldLabel(label,min,width,dl,parent,previousMax);return changed; }
    internal static bool SliderInt(string label,ref int value,int min,int max,string format="%d",ImGuiSliderFlags flags=ImGuiSliderFlags.None)
    { using var height=MaterialText.PushLineHeight(Visible(label));var width=FitField(label);var p=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,p,width,dl);var changed=ImGui.SliderInt(label,ref value,min,max,UiText.T(format),flags);if(clipped)dl.PopClipRect();FieldLabel(label,p,width,dl,parent,previousMax);return changed; }
    internal static bool Combo(string label,ref int value,string[] options,int count)
    {
        var changed=false;
        if (BeginCombo(label,value>=0 && value<count?options[value]:string.Empty))
        {
            for(var index=0;index<count;index++)
            { ImGui.PushID(index);if (Selectable(options[index],value==index)) { changed=value!=index;value=index; }if (value==index) ImGui.SetItemDefaultFocus();ImGui.PopID(); }
            ImGui.EndCombo();
        }
        return changed;
    }
    internal static bool Combo(string label,ref int value,string options)
    { var items=options.Split('\0').Where(v=>v.Length>0).ToArray();return Combo(label,ref value,items,items.Length); }
    internal static bool BeginPopupModal(string original,ImGuiWindowFlags flags)
    {
        ImGui.SetNextWindowSize(new Vector2(520*MaterialTheme.Metrics.Scale,0),ImGuiCond.Always);
        var open=ImGui.BeginPopupModal(original,flags);if(open) Title(original.Split("##",2)[0]);return open;
    }
    internal static void Title(string original,string? display=null)
        => TitleWithButtons(original, display, null);

    internal static void ReserveTitleSpace(Window owner, string visible, float minimumWidth)
    {
        var style = ImGui.GetStyle();
        var fontSize = ImGui.GetFontSize();
        var collapse = (owner.Flags & (ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.Modal)) == 0
            && style.WindowMenuButtonPosition != ImGuiDir.None;
        var controls = AdditionalTitleButtonWidth(owner, fontSize)
            + ((owner.ShowCloseButton ? 1 : 0) + (collapse ? 1 : 0)) * (fontSize + style.ItemInnerSpacing.X);
        var required = (MaterialText.Measure(visible).X + controls + style.FramePadding.X * 2 + style.ItemInnerSpacing.X)
            / ImGui.GetIO().FontGlobalScale;
        var bounds = owner.SizeConstraints ?? new WindowSizeConstraints();
        bounds.MinimumSize = new(Math.Max(minimumWidth, required), bounds.MinimumSize.Y);
        owner.SizeConstraints = bounds;
    }

    private static float AdditionalTitleButtonWidth(Window? owner, float fontSize)
    {
        if (owner is null) return 0;
        var count = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
        if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) count++;
        return count * (fontSize + ImGui.GetStyle().ItemInnerSpacing.X);
    }

    internal static void TitleWithButtons(string original,string? display, Window? owner)
    {
        var translated=display ?? UiText.Status(original);if (translated==original) return;
        var style=ImGui.GetStyle();var fontSize=ImGui.GetFontSize();var height=ImGui.GetFrameHeight();
        var flags=ImGuiP.GetCurrentWindow().Flags;
        var collapseLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && style.WindowMenuButtonPosition==ImGuiDir.Left;
        var p=ImGui.GetWindowPos()+new Vector2(style.FramePadding.X+(collapseLeft?fontSize+style.ItemInnerSpacing.X:0),style.FramePadding.Y);
        var reserved = owner is null ? 2 * height : style.FramePadding.X * 2
            + (owner.ShowCloseButton ? fontSize : 0) + AdditionalTitleButtonWidth(owner, fontSize);
        if (owner is not null && (flags & ImGuiWindowFlags.NoCollapse) == 0 && style.WindowMenuButtonPosition == ImGuiDir.Right)
            reserved += fontSize + style.ItemInnerSpacing.X;
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(ImGui.GetWindowPos(),ImGui.GetWindowPos()+new Vector2(Math.Max(0, ImGui.GetWindowSize().X-reserved),height),false);
        var background=style.Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(p,p+new Vector2(Math.Max(ImGui.CalcTextSize(original).X,MaterialText.Measure(translated).X),height-style.FramePadding.Y),ImGui.ColorConvertFloat4ToU32(background));
        MaterialText.AddText(dl,p,ImGui.GetColorU32(ImGuiCol.Text),translated);dl.PopClipRect();
    }

    internal static void TableHeadersRow()
    {
        var captions=Enumerable.Range(0,ImGui.TableGetColumnCount()).Select(index=>UiText.T(ImGui.TableGetColumnName(index))).ToArray();
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers,captions.Any(MaterialText.RequiresShaping)?captions.Select(text=>MaterialText.Measure(text).Y).DefaultIfEmpty(0).Max():0);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            TableHeader(ImGui.TableGetColumnName(index));
        }
    }
    internal static void TableHeader(string original)
    {
        var translated=UiText.T(original);
        var p=ImGui.GetCursorScreenPos();var width=ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);ImGui.TableHeader(original);ImGui.PopStyleColor();
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(p,p+new Vector2(Math.Max(1,width),Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(translated).Y)),true);
        MaterialText.AddText(dl,p,ImGui.GetColorU32(ImGuiCol.Text),translated);dl.PopClipRect();
        if(MaterialText.Measure(translated).X>width && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
    }
    internal static bool BeginTabItem(string label,ref bool open,ImGuiTabItemFlags flags=ImGuiTabItemFlags.None,bool translate=true)
    {
        using var height=TabHeight();
        var raw=label.Split("##",2)[0];
        var shown=translate?Visible(label):raw;
        ImGui.SetNextItemWidth(Math.Max(ImGui.CalcTextSize(raw).X,MaterialText.Measure(shown).X)+ImGui.GetStyle().FramePadding.X*2+ImGui.GetFontSize()+12*MaterialTheme.Metrics.Scale);
        var drawing=ImGui.GetWindowDrawList();var first=drawing.VtxBuffer.Size;
        var visible=ImGui.BeginTabItem(label,ref open,flags);
        try { TranslatedTabCaption(drawing,first,raw,shown,true); }
        catch { if(visible)ImGui.EndTabItem();throw; }
        return visible;
    }
    private static unsafe void TranslatedTabCaption(ImDrawListPtr drawing,int first,string raw,string shown,bool close)
    {
        if(raw==shown && !MaterialText.RequiresShaping(shown))return;
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        // Keep native arrows, closing and translucent backgrounds intact. Only
        // the original caption's emitted glyph quads lose their alpha.
        var font=ImGui.GetFont();
        for(var index=first;index+3<drawing.VtxBuffer.Size;index++)
        {
            var a=drawing.VtxBuffer[index];var b=drawing.VtxBuffer[index+1];
            var c=drawing.VtxBuffer[index+2];var d=drawing.VtxBuffer[index+3];
            if(a.Uv.X>=c.Uv.X||a.Uv.Y>=c.Uv.Y||a.Uv.Y!=b.Uv.Y||b.Uv.X!=c.Uv.X
                ||c.Uv.Y!=d.Uv.Y||d.Uv.X!=a.Uv.X||a.Pos.Y!=b.Pos.Y||b.Pos.X!=c.Pos.X
                ||c.Pos.Y!=d.Pos.Y||d.Pos.X!=a.Pos.X||a.Pos.Y<min.Y-1||c.Pos.Y>max.Y+1)continue;
            foreach(var character in raw)
            {
                var glyph=ImGui.FindGlyphNoFallback(font,character);
                if(glyph.Handle==null)glyph=new ImFontGlyphPtr(font.Handle->FallbackGlyph);
                if(glyph.Handle==null||glyph.Handle->U0>=glyph.Handle->U1||glyph.Handle->V0>=glyph.Handle->V1)continue;
                if(a.Uv.X<glyph.Handle->U0-.00001f||a.Uv.Y<glyph.Handle->V0-.00001f
                    ||c.Uv.X>glyph.Handle->U1+.00001f||c.Uv.Y>glyph.Handle->V1+.00001f)continue;
                for(var offset=0;offset<4;offset++)
                    drawing.Handle->VtxBuffer.Data[index+offset].Col&=0x00FFFFFF;
                index+=3;break;
            }
        }
        var bar=ImGui.GetCurrentContext().CurrentTabBar;
        var clipMin=new Vector2(Math.Max(min.X,bar.ScrollingRectMinX),min.Y);
        var clipMax=new Vector2(Math.Min(max.X-(close?ImGui.GetFontSize():0),bar.ScrollingRectMaxX),max.Y);
        if(clipMax.X<=clipMin.X)return;
        Ink(shown,min+new Vector2(ImGui.GetStyle().FramePadding.X,(max.Y-min.Y-MaterialText.Measure(shown).Y)*.5f),clipMin,clipMax);
    }
    internal static bool SliderFloat(string label,ref float value,float min,float max,string format="%.3f",ImGuiSliderFlags flags=ImGuiSliderFlags.None)
    { using var height=MaterialText.PushLineHeight(Visible(label));var width=FitField(label);var p=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,p,width,dl);var changed=ImGui.SliderFloat(label,ref value,min,max,format,flags);if(clipped)dl.PopClipRect();FieldLabel(label,p,width,dl,parent,previousMax);return changed; }
    internal static bool ColorEdit4(string label,ref Vector4 value,ImGuiColorEditFlags flags=ImGuiColorEditFlags.None)
    { using var height=MaterialText.PushLineHeight(Visible(label));var width=FitField(label,minimumPixels:320*MaterialTheme.Metrics.Scale);var p=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,p,width,dl);var changed=ImGui.ColorEdit4(label,ref value,flags);if(clipped)dl.PopClipRect();FieldLabel(label,p,width,dl,parent,previousMax);return changed; }
    internal static bool MenuItem(string original)
    {
        var translated=Visible(original);using var height=MaterialText.PushLineHeight(translated);ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var clicked=ImGui.MenuItem(original);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();Ink(translated,min+ImGui.GetStyle().FramePadding,min,max);return clicked;
    }
    internal static bool BeginMenu(string original)
    {
        var translated=Visible(original);using var height=MaterialText.PushLineHeight(translated);var parent=ImGui.GetWindowDrawList();
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.BeginMenu(original);ImGui.PopStyleColor();
        // Popup Begin changes the current window; the native menu label belongs to the parent draw list.
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();parent.PushClipRect(min,max,true);MaterialText.AddText(parent,min+new Vector2(ImGui.GetStyle().FramePadding.X,(max.Y-min.Y-MaterialText.Measure(translated).Y)*.5f),ImGui.GetColorU32(ImGuiCol.Text),translated);parent.PopClipRect();
        var arrowSize=ImGui.GetFontSize()*.25f;
        var arrowCenter=new Vector2(max.X-ImGui.GetStyle().FramePadding.X-ImGui.GetFontSize()*.5f,(min.Y+max.Y)*.5f);
        parent.AddTriangleFilled(arrowCenter+new Vector2(-arrowSize,-arrowSize),arrowCenter+new Vector2(-arrowSize,arrowSize),
            arrowCenter+new Vector2(arrowSize,0),ImGui.GetColorU32(ImGuiCol.Text));
        return open;
    }
}
