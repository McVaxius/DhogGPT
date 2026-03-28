using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DhogGPT.Windows;

public sealed class FirstUseGuideWindow : Window, IDisposable
{
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

    public override void Draw()
    {
        ImGui.TextWrapped("DhogGPT works best in Simple + Compact mode: translated conversations live in tabs, and the bottom composer sends translated chat without leaving the main window.");
        ImGui.Separator();

        ImGui.BulletText("Open the main window with /dhoggpt or /dgpt.");
        ImGui.BulletText("Start in Simple chat mode with Compact enabled for the all-in-one tabbed chat view.");
        ImGui.BulletText("Use the pinned channel tabs for general chat, the + button for New DM tabs, and Recent for older DM threads.");
        ImGui.BulletText("Pick incoming and outgoing languages in Settings. Leave source on Auto unless you know it.");
        ImGui.BulletText("Use Krangle if you want display-only name scrambling in the plugin window.");
        ImGui.BulletText("Click the DTR entry to open the DhogGPT main window.");

        ImGui.Spacing();
        ImGui.TextWrapped("There is still a fuller non-compact path in the main window, but Compact mode is the primary chat UX now. If one translation endpoint fails, DhogGPT automatically rolls to the next configured fallback.");

        if (ImGui.Button("Open main window"))
            plugin.OpenMainUi();

        ImGui.SameLine();
        if (ImGui.Button("Open settings"))
            plugin.OpenConfigUi();

        ImGui.SameLine();
        if (ImGui.Button("Got it"))
        {
            plugin.MarkFirstUseGuideSeen();
            IsOpen = false;
        }
    }
}
