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
        ImGui.TextWrapped("DhogGPT translates selected chat channels into Echo and gives you a send-confirmed composer for outgoing translated messages.");
        ImGui.Separator();

        ImGui.BulletText("Open the main window with /dhoggpt or /dgpt.");
        ImGui.BulletText("Pick your incoming target language in Settings.");
        ImGui.BulletText("Leave source language on Auto unless you know the source language.");
        ImGui.BulletText("Choose which incoming channels should be translated.");
        ImGui.BulletText("Use Preview translation before Translate and send the first time you test a new channel.");
        ImGui.BulletText("Click the DTR entry to quickly toggle the plugin on or off.");

        ImGui.Spacing();
        ImGui.TextWrapped("If one web endpoint starts failing, DhogGPT will try the next fallback endpoint listed in Settings.");

        if (ImGui.Button("Open main window"))
            plugin.ToggleMainUi();

        ImGui.SameLine();
        if (ImGui.Button("Open settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        if (ImGui.Button("Got it"))
        {
            plugin.MarkFirstUseGuideSeen();
            IsOpen = false;
        }
    }
}
