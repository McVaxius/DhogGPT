using System;
using System.Linq;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using DhogGPT.Windows;

namespace DhogGPT;

public sealed partial class Plugin
{
    private MaterialGlassRenderer? backgroundGlass;
    private bool backgroundWasBlurred;
    internal bool BackgroundRequested => Configuration.UiStylingEnabled && Configuration.UiColors is { } choices && (choices.HasBackgroundBlur(choices.Mode) || choices.Mode == MaterialColorMode.Glassmorphism && choices.GlassBackground == MaterialGlassBackground.Image);
    private readonly FileDialogManager glassImagePicker = new();
    private bool backgroundImagePickerOpen;

    private void DrawUi()
    {
        RefreshUiText();
        if (!WindowSystem.Windows.Any(window => window.IsOpen))
        {
            if (backgroundImagePickerOpen) glassImagePicker.Draw();
            if (uiFonts is not null) ReleaseWindowResources();
            return;
        }

        ApplyUiAppearance();
        var completed = false;
        try
        {
            if (backgroundImagePickerOpen)
                using (MaterialTheme.Push(uiTheme, ImGuiHelpers.GlobalScale, Configuration.UiStylingEnabled ? MaterialStyleMode.ColorsOnly : MaterialStyleMode.Native))
                    glassImagePicker.Draw();
            var choices = Configuration.UiColors ?? new();
            var blur = Configuration.UiStylingEnabled && choices.HasBackgroundBlur(choices.Mode);
            var image = Configuration.UiStylingEnabled && choices.Mode == MaterialColorMode.Glassmorphism && choices.GlassBackground == MaterialGlassBackground.Image;
            if ((blur || image) && uiFonts.Ready)
            {
                backgroundGlass ??= new(PluginInterface, Log);
                if (backgroundWasBlurred && !blur) backgroundGlass.ReleaseResources();
                backgroundGlass.BeginFrame(blur, image);
                backgroundGlass.ConfigureImage(Textures, image, choices.GlassImagePath);
                backgroundWasBlurred = blur;
            }
            else
            {
                DisposeBackgroundGlass();
            }
            using var background = MaterialSkin.PushBackgroundRenderer(backgroundGlass, blur, choices.BackgroundBlurPercent / 100f);
            DrawUiContent();
            completed = true;
        }
        finally { backgroundGlass?.EndFrame(completed); }
    }

    private void DisposeBackgroundGlass()
    {
        backgroundGlass?.Dispose();
        backgroundGlass = null;
        backgroundWasBlurred = false;
    }

    private void ReleaseUiSounds()
    {
        uiSounds?.Dispose();
        uiSounds = null;
    }

    private void ReleaseWindowResources()
    {
        DisposeBackgroundGlass();
        ReleaseUiSounds();
        uiFonts?.Dispose();
        uiFonts = null!;
        shapedText?.Dispose();
        shapedText = null!;
        uiTheme = null!;
        uiLanguages = null!;
    }

    private void ReleaseGlassResources() => backgroundGlass?.ReleaseResources();
    private void ToggleUiStyling() { Configuration.UiStylingEnabled = !Configuration.UiStylingEnabled; Configuration.Save(); }
    private void ToggleWindowLock() { Configuration.UiWindowsLocked = !Configuration.UiWindowsLocked; Configuration.Save(); }

    internal void PrepareWindowBackground(MaterialWindowOpacity opacity)
    {
        if (backgroundGlass != null) backgroundGlass.Opacity = Configuration.UiTransparencyEnabled
            ? ImGui.IsWindowAppearing() ? float.IsFinite(Configuration.FocusedWindowOpacity) ? Math.Clamp(Configuration.FocusedWindowOpacity, .1f, 1f) : 1
                : opacity.CurrentOpacity
            : 1;
    }

    private void DrawUiWindowControls()
    {
        var styled = Configuration.UiStylingEnabled;
        if (UiGui.Checkbox("UI styling", ref styled)) { Configuration.UiStylingEnabled = styled; Configuration.Save(); }
        var locked = Configuration.UiWindowsLocked;
        if (UiGui.Checkbox("Lock window movement and resizing", ref locked)) { Configuration.UiWindowsLocked = locked; Configuration.Save(); }
    }

    private void BrowseBackgroundImage()
    {
        glassImagePicker.OpenFileDialog(
    UiText.T("Choose background image") + "###DhogGPTBackgroundImagePicker",
    UiText.T("Images") + "{.png,.jpg,.jpeg,.bmp,.gif,.webp,.tif,.tiff,.dds,.tga},.*",
    (accepted, path) =>
    {
        backgroundImagePickerOpen = false;
        if (!accepted) return;
        colorDraft = colorDraft with { GlassImagePath = path };
        Configuration.UiColors = colorDraft;
        Configuration.Save();
    });
        backgroundImagePickerOpen = true;
    }

    private void DrawBackgroundSettings()
    {
        var choices = Configuration.UiColors ?? new();
        var blur = choices.HasBackgroundBlur(choices.Mode);
        var changed = UiGui.Checkbox("Background blur", ref blur);
        if (changed) choices = choices.WithBackgroundBlur(choices.Mode, blur);
        var frost = choices.BackgroundBlurPercent;
        MaterialText.Text(UiText.T("Blur (%)"));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(160 * MaterialTheme.Metrics.Scale, 80 * MaterialTheme.Metrics.Scale));
        using (MaterialSkin.NativeFieldStyle())
            if (ImGui.InputInt("##BackgroundFrost", ref frost)) { choices = choices with { BackgroundBlurPercent = frost }; changed = true; }
        if (changed) { Configuration.UiColors = colorDraft = choices; Configuration.Save(); }
        DrawGlassStatusAndScreenshots();
    }

    private void DrawGlassStatusAndScreenshots()
    {
        if (colorDraft.Mode == MaterialColorMode.Glassmorphism && colorDraft.GlassBackground == MaterialGlassBackground.Image)
        {
            MaterialText.TextWrapped(UiText.T(backgroundGlass?.ImageProblem ?? (backgroundGlass?.ImageAvailable == true
                ? "Image backgrounds remain visible to screenshots." : string.IsNullOrWhiteSpace(colorDraft.GlassImagePath) ? "Choose an image file." : "Loading image...")));
            return;
        }
        var paused = backgroundGlass?.ScreenshotsPaused == true;
        ImGui.BeginDisabled(!paused && backgroundGlass?.CanPauseForScreenshots != true);
        if (UiGui.Checkbox("Pause backdrop for screenshots", ref paused)) backgroundGlass?.TrySetScreenshotsPaused(paused);
        ImGui.EndDisabled();
    }
}
