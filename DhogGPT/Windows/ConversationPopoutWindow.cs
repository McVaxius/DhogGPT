using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DhogGPT.Windows;

internal sealed class ConversationPopoutWindow : Window
{
    private readonly MainWindow owner;
    private readonly string conversationKey;

    public ConversationPopoutWindow(MainWindow owner, string conversationKey)
        : base($"Conversation###DhogGPTPopout:{conversationKey}", ImGuiWindowFlags.NoCollapse)
    {
        this.owner = owner;
        this.conversationKey = conversationKey;

        Size = new Vector2(480f, 360f);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        WindowName = owner.GetConversationPopoutWindowTitle(conversationKey);
    }

    public override void Draw()
    {
        owner.DrawConversationPopout(conversationKey);
    }

    public override void OnClose()
    {
        owner.HandleConversationPopoutClosed(conversationKey);
    }
}
