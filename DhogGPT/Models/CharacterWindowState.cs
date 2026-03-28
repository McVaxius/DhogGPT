using System.Numerics;

namespace DhogGPT.Models;

[Serializable]
public sealed class CharacterWindowState
{
    public SavedWindowPosition MainWindow { get; set; } = new();
    public SavedWindowPosition SettingsWindow { get; set; } = new();
}

[Serializable]
public sealed class SavedWindowPosition
{
    public float X { get; set; } = 1f;
    public float Y { get; set; } = 1f;
    public bool HasValue { get; set; }

    public void Set(Vector2 position)
    {
        X = position.X;
        Y = position.Y;
        HasValue = true;
    }

    public void Reset(float x = 1f, float y = 1f)
    {
        X = x;
        Y = y;
        HasValue = true;
    }

    public Vector2 ToVector2()
        => new(X, Y);
}
