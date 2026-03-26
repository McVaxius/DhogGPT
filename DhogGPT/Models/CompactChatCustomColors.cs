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

    public Vector4 GetInboundHeader() => ToVector4(InboundHeader, new Vector4(0.58f, 0.80f, 1.0f, 1.0f));
    public Vector4 GetInboundTranslation() => ToVector4(InboundTranslation, new Vector4(0.84f, 0.92f, 1.0f, 1.0f));
    public Vector4 GetOutboundHeader() => ToVector4(OutboundHeader, new Vector4(0.66f, 0.96f, 0.72f, 1.0f));
    public Vector4 GetOutboundTranslation() => ToVector4(OutboundTranslation, new Vector4(0.86f, 1.0f, 0.90f, 1.0f));
    public Vector4 GetError() => ToVector4(Error, new Vector4(1.0f, 0.55f, 0.55f, 1.0f));

    public void SetInboundHeader(Vector4 value) => InboundHeader = ToArray(value);
    public void SetInboundTranslation(Vector4 value) => InboundTranslation = ToArray(value);
    public void SetOutboundHeader(Vector4 value) => OutboundHeader = ToArray(value);
    public void SetOutboundTranslation(Vector4 value) => OutboundTranslation = ToArray(value);
    public void SetError(Vector4 value) => Error = ToArray(value);

    private static Vector4 ToVector4(float[]? values, Vector4 fallback)
    {
        if (values == null || values.Length < 4)
            return fallback;

        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static float[] ToArray(Vector4 value)
        => [value.X, value.Y, value.Z, value.W];
}
