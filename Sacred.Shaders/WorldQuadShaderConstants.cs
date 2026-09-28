using System.Numerics;

namespace Sacred.Shaders;

/// <summary>Root constants consumed by the world-quad shaders.</summary>
public readonly record struct WorldQuadShaderConstants(
    Vector4 Rect,
    Vector2 ViewportSize,
    Vector3 AmbientColour,
    bool IsPremultipliedAlpha,
    float PaperWhiteNits,
    Vector2 CameraMotionPixels = default,
    float HistoryWeight = 0.0f,
    float HistoryUvScale = 1.0f)
{
    public const int FloatCount = 16;
}

/// <summary>Serializes world-quad values in the order declared by HLSL.</summary>
public sealed class WorldQuadShaderConstantsUpdater
{
    public unsafe void Write(float* target, in WorldQuadShaderConstants constants)
    {
        target[0] = constants.Rect.X;
        target[1] = constants.Rect.Y;
        target[2] = constants.Rect.Z;
        target[3] = constants.Rect.W;
        target[4] = constants.ViewportSize.X;
        target[5] = constants.ViewportSize.Y;
        target[6] = constants.IsPremultipliedAlpha ? 1.0f : 0.0f;
        target[7] = Math.Max(0.0f, constants.PaperWhiteNits);
        target[8] = Math.Max(0.0f, constants.AmbientColour.X);
        target[9] = Math.Max(0.0f, constants.AmbientColour.Y);
        target[10] = Math.Max(0.0f, constants.AmbientColour.Z);
        target[11] = float.IsFinite(constants.HistoryUvScale) && constants.HistoryUvScale > 0.0f
            ? constants.HistoryUvScale
            : 1.0f;
        target[12] = constants.CameraMotionPixels.X;
        target[13] = constants.CameraMotionPixels.Y;
        target[14] = Math.Clamp(constants.HistoryWeight, 0.0f, 1.0f);
        target[15] = 0.0f;
    }
}
