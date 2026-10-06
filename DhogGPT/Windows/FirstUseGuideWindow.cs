using AethertekUI;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DhogGPT.Windows;

public sealed class FirstUseGuideWindow : Window, IDisposable
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion windowMotion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private readonly Plugin plugin;

    public FirstUseGuideWindow(Plugin plugin)
        : base("Welcome To DhogGPT###DhogGPTFirstUse", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        IsOpen = !plugin.Configuration.HasSeenFirstUseGuide;
        RespectCloseHotkey = true;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520f, 300f),
            MaximumSize = new Vector2(900f, 700f),
        };
    }

    public void Dispose()
    {
    }

    public override void OnClose()
    {
        plugin.MarkFirstUseGuideSeen();
    }

    public override void PreDraw()
    {
        windowMotion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        windowMotion.Restore(this);
        plugin.ApplyWindowOpacity(windowOpacity, WindowName);
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        UiGui.Title("Welcome To DhogGPT");
        UiGui.TextWrapped("DhogGPT now defaults to ultra compact mode. Translated conversations live in tabs, the bottom composer sends translated chat without leaving the main window, and settings let you choose whether vanilla chat stays visible alongside DhogGPT.");
        ImGui.Separator();

        UiGui.BulletText("Open the main window with /dhoggpt, /dgpt, or /dog.");
        UiGui.BulletText("Use /dgpt ultra to toggle between regular mode and ultra compact mode.");
        UiGui.BulletText("Use the pinned channel tabs for general chat, the + button for New DM tabs, H for hidden channels, and R for recent DM threads.");
        UiGui.BulletText("Press / or Enter while ultra compact mode is open but unfocused to jump straight back into the DhogGPT composer.");
        UiGui.BulletText("Ultra compact mode uses the active tab as the destination, so there is no separate chat-type dropdown or Send button there.");
        UiGui.BulletText("Raw slash commands typed into DhogGPT send directly and leave a Safe breadcrumb instead of going through translation.");
        UiGui.BulletText("Pick incoming and outgoing languages in Settings. Leave source on Auto unless you know it.");
        UiGui.BulletText("Use Krangle if you want display-only name scrambling in the plugin window.");
        UiGui.BulletText("Click the DTR entry to open the DhogGPT main window.");

        ImGui.Spacing();
        UiGui.TextWrapped("Regular mode still keeps the fuller translator surface available, but compact and super compact are deprecated and now route into ultra compact. If one translation endpoint fails, DhogGPT automatically rolls to the next configured fallback.");

        if (UiGui.Button("Open main window"))
            plugin.OpenMainUi();
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Open the DhogGPT main chat window.");

        DhogGptPresentation.SameLineIfFits(MaterialText.Measure(UiText.T("Open settings")).X+ImGui.GetStyle().FramePadding.X*2);
        if (UiGui.Button("Open settings"))
            plugin.OpenConfigUi();
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Open the DhogGPT settings window.");

        DhogGptPresentation.SameLineIfFits(MaterialText.Measure(UiText.T("Got it")).X+ImGui.GetStyle().FramePadding.X*2);
        if (UiGui.Button("Got it"))
        {
            plugin.MarkFirstUseGuideSeen();
            IsOpen = false;
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Close this guide and mark it as seen.");
    }
}
