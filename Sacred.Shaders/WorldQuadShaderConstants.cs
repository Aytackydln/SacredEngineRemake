using System.Numerics;

namespace Sacred.Shaders;

/// <summary>Root constants consumed by the world-quad shaders.</summary>
public readonly record struct WorldQuadShaderConstants(
    Vector4 Rect,
    Vector2 ViewportSize,
    Vector3 AmbientColour,
    bool IsPremultipliedAlpha,
    float PaperWhiteNits)
{
    public const int FloatCount = 12;
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
        target[11] = 0.0f;
    }
}
