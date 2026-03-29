using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace DhogGPT.Managers;

public sealed class LinkPayloadManager : IDisposable
{
    private readonly Dictionary<uint, Action<uint, SeString>> handlers = [];
    private uint nextCommandId = 1;

    public DalamudLinkPayload Register(Action<uint, SeString> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Register(nextCommandId++, handler);
    }

    public DalamudLinkPayload Register(uint commandId, Action<uint, SeString> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (handlers.ContainsKey(commandId))
            Unregister(commandId);

        handlers[commandId] = handler;
        return Plugin.ChatGui.AddChatLinkHandler(commandId, HandleRegisteredLink);
    }

    public void Unregister(uint commandId)
    {
        if (!handlers.Remove(commandId))
            return;

        Plugin.ChatGui.RemoveChatLinkHandler(commandId);
    }

    public void UnregisterAll()
    {
        handlers.Clear();
        Plugin.ChatGui.RemoveChatLinkHandler();
    }

    public void Dispose() => UnregisterAll();

    private void HandleRegisteredLink(uint commandId, SeString payload)
    {
        if (!handlers.TryGetValue(commandId, out var handler))
            return;

        handler(commandId, payload);
    }
}
