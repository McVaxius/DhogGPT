using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DhogGPT.Services.Chat;

public sealed unsafe class VanillaChatWindowService : IDisposable
{
    private static readonly string[] ChatAddonNames =
    [
        "ChatLog",
        "ChatLogPanel_0",
        "ChatLogPanel_1",
        "ChatLogPanel_2",
        "ChatLogPanel_3",
    ];

    private readonly Func<bool> shouldSuppressVanillaChat;
    private bool suppressionApplied;

    public VanillaChatWindowService(Func<bool> shouldSuppressVanillaChat)
    {
        this.shouldSuppressVanillaChat = shouldSuppressVanillaChat;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        SetChatInteractable(true);
    }

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (!shouldSuppressVanillaChat())
        {
            if (!suppressionApplied)
                return;

            suppressionApplied = false;
            SetChatInteractable(true);
            return;
        }

        suppressionApplied = true;
        foreach (var name in ChatAddonNames)
        {
            if (IsAddonInteractable(name))
                SetAddonInteractable(name, false);
        }
    }

    private static T* GetAddon<T>(string name)
        where T : unmanaged
    {
        var addon = RaptureAtkModule.Instance()->RaptureAtkUnitManager.GetAddonByName(name);
        return addon != null && addon->IsReady ? (T*)addon : null;
    }

    private static void SetAddonInteractable(string name, bool interactable)
    {
        var addon = GetAddon<AtkUnitBase>(name);
        if (addon == null)
            return;

        addon->IsVisible = interactable;
    }

    private static bool IsAddonInteractable(string name)
    {
        var addon = GetAddon<AtkUnitBase>(name);
        return addon != null && addon->IsVisible;
    }

    private static void SetChatInteractable(bool interactable)
    {
        foreach (var name in ChatAddonNames)
            SetAddonInteractable(name, interactable);
    }
}
