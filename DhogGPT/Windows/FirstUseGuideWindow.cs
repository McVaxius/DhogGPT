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
        ImGui.TextWrapped("DhogGPT works best in Simple + Compact mode, and ultra compact mode is the intended vanilla-chat replacement path. Translated conversations live in tabs, and the bottom composer sends translated chat without leaving the main window.");
        ImGui.Separator();

        ImGui.BulletText("Open the main window with /dhoggpt, /dgpt, or /dog.");
        ImGui.BulletText("Use /dgpt ultra to toggle ultra compact mode, which replaces the vanilla chat window while DhogGPT is open.");
        ImGui.BulletText("Use the pinned channel tabs for general chat, the + button for New DM tabs, H for hidden channels, and R for recent DM threads.");
        ImGui.BulletText("Press / or Enter while ultra compact mode is open but unfocused to jump straight back into the DhogGPT composer.");
        ImGui.BulletText("Ultra compact mode uses the active tab as the destination, so there is no separate chat-type dropdown or Send button there.");
        ImGui.BulletText("Raw slash commands typed into DhogGPT send directly and leave an Echo breadcrumb instead of going through translation.");
        ImGui.BulletText("Pick incoming and outgoing languages in Settings. Leave source on Auto unless you know it.");
        ImGui.BulletText("Use Krangle if you want display-only name scrambling in the plugin window.");
        ImGui.BulletText("Click the DTR entry to open the DhogGPT main window.");

        ImGui.Spacing();
        ImGui.TextWrapped("There is still a fuller non-compact path in the main window, but compact and ultra compact mode are the primary chat UX now. If one translation endpoint fails, DhogGPT automatically rolls to the next configured fallback.");

        if (ImGui.Button("Open main window"))
            plugin.OpenMainUi();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open the DhogGPT main chat window.");

        ImGui.SameLine();
        if (ImGui.Button("Open settings"))
            plugin.OpenConfigUi();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open the DhogGPT settings window.");

        ImGui.SameLine();
        if (ImGui.Button("Got it"))
        {
            plugin.MarkFirstUseGuideSeen();
            IsOpen = false;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Close this guide and mark it as seen.");
    }
}
