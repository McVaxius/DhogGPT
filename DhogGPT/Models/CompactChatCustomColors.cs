using System.Numerics;

namespace DhogGPT.Models;

[Serializable]
public sealed class CompactChatCustomColors
{
    public float[] InboundHeader { get; set; } = [0.58f, 0.80f, 1.0f, 1.0f];
    public float[] InboundTranslation { get; set; } = [0.84f, 0.92f, 1.0f, 1.0f];
    public float[] OutboundHeader { get; set; } = [0.66f, 0.96f, 0.72f, 1.0f];
    public float[] OutboundTranslation { get; set; } = [0.86f, 1.0f, 0.90f, 1.0f];
    public float[] Error { get; set; } = [1.0f, 0.55f, 0.55f, 1.0f];
    public float[] Tab { get; set; } = [];
    public float[] TabHovered { get; set; } = [];
    public float[] TabActive { get; set; } = [];
    public float[] TabUnfocused { get; set; } = [];
    public float[] TabUnfocusedActive { get; set; } = [];
    public float[] TabText { get; set; } = [];
    public float[] ActiveTabText { get; set; } = [];

    public Vector4 GetInboundHeader() => ToVector4(InboundHeader, new Vector4(0.58f, 0.80f, 1.0f, 1.0f));
    public Vector4 GetInboundTranslation() => ToVector4(InboundTranslation, new Vector4(0.84f, 0.92f, 1.0f, 1.0f));
    public Vector4 GetOutboundHeader() => ToVector4(OutboundHeader, new Vector4(0.66f, 0.96f, 0.72f, 1.0f));
    public Vector4 GetOutboundTranslation() => ToVector4(OutboundTranslation, new Vector4(0.86f, 1.0f, 0.90f, 1.0f));
    public Vector4 GetError() => ToVector4(Error, new Vector4(1.0f, 0.55f, 0.55f, 1.0f));
    public Vector4 GetTab() => ToVector4(Tab, GetDerivedTabPalette().Tab);
    public Vector4 GetTabHovered() => ToVector4(TabHovered, GetDerivedTabPalette().TabHovered);
    public Vector4 GetTabActive() => ToVector4(TabActive, GetDerivedTabPalette().TabActive);
    public Vector4 GetTabUnfocused() => ToVector4(TabUnfocused, GetDerivedTabPalette().TabUnfocused);
    public Vector4 GetTabUnfocusedActive() => ToVector4(TabUnfocusedActive, GetDerivedTabPalette().TabUnfocusedActive);
    public Vector4 GetTabText() => ToVector4(TabText, GetReadableTextColor(GetTab()));
    public Vector4 GetActiveTabText() => ToVector4(ActiveTabText, GetReadableTextColor(GetTabActive()));

    public void SetInboundHeader(Vector4 value) => InboundHeader = ToArray(value);
    public void SetInboundTranslation(Vector4 value) => InboundTranslation = ToArray(value);
    public void SetOutboundHeader(Vector4 value) => OutboundHeader = ToArray(value);
    public void SetOutboundTranslation(Vector4 value) => OutboundTranslation = ToArray(value);
    public void SetError(Vector4 value) => Error = ToArray(value);
    public void SetTab(Vector4 value) => Tab = ToArray(value);
    public void SetTabHovered(Vector4 value) => TabHovered = ToArray(value);
    public void SetTabActive(Vector4 value) => TabActive = ToArray(value);
    public void SetTabUnfocused(Vector4 value) => TabUnfocused = ToArray(value);
    public void SetTabUnfocusedActive(Vector4 value) => TabUnfocusedActive = ToArray(value);
    public void SetTabText(Vector4 value) => TabText = ToArray(value);
    public void SetActiveTabText(Vector4 value) => ActiveTabText = ToArray(value);

    private static Vector4 ToVector4(float[]? values, Vector4 fallback)
    {
        if (values == null || values.Length < 4)
            return fallback;

        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static float[] ToArray(Vector4 value)
        => [value.X, value.Y, value.Z, value.W];

    private (Vector4 Tab, Vector4 TabHovered, Vector4 TabActive, Vector4 TabUnfocused, Vector4 TabUnfocusedActive) GetDerivedTabPalette()
    {
        var accent = BlendColors(GetInboundHeader(), GetOutboundHeader(), 0.5f);
        return (
            WithAlpha(ScaleColorRgb(accent, 0.48f), 0.78f),
            WithAlpha(ScaleColorRgb(accent, 0.90f), 0.92f),
            WithAlpha(ScaleColorRgb(accent, 1.12f), 0.98f),
            WithAlpha(ScaleColorRgb(accent, 0.36f), 0.55f),
            WithAlpha(ScaleColorRgb(accent, 0.72f), 0.78f));
    }

    private static Vector4 BlendColors(Vector4 left, Vector4 right, float amount)
    {
        var t = Math.Clamp(amount, 0f, 1f);
        return new Vector4(
            left.X + ((right.X - left.X) * t),
            left.Y + ((right.Y - left.Y) * t),
            left.Z + ((right.Z - left.Z) * t),
            left.W + ((right.W - left.W) * t));
    }

    private static Vector4 ScaleColorRgb(Vector4 color, float scale)
        => new(
            Math.Clamp(color.X * scale, 0f, 1f),
            Math.Clamp(color.Y * scale, 0f, 1f),
            Math.Clamp(color.Z * scale, 0f, 1f),
            color.W);

    private static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, Math.Clamp(alpha, 0f, 1f));

    private static Vector4 GetReadableTextColor(Vector4 background)
    {
        var luminance = (0.2126f * background.X) + (0.7152f * background.Y) + (0.0722f * background.Z);
        return luminance >= 0.58f
            ? new Vector4(0.05f, 0.05f, 0.05f, 1.0f)
            : new Vector4(0.95f, 0.97f, 1.0f, 1.0f);
    }
}
