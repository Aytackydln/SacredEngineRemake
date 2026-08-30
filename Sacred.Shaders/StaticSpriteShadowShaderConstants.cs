using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Shaders;

[StructLayout(LayoutKind.Sequential)]
public readonly struct StaticSpriteShadowInstance(
    float rootX,
    float rootY,
    float contactExtent,
    float projectionLength,
    uint atlasCellAndProjection)
{
    public readonly float RootX = rootX;
    public readonly float RootY = rootY;
    public readonly float ContactExtent = contactExtent;
    public readonly float ProjectionLength = projectionLength;
    public readonly uint AtlasCellAndProjection = atlasCellAndProjection;
}

public readonly record struct StaticSpriteShadowSceneConstants(
    Vector2 ViewportSize,
    float Opacity,
    Vector2 Projection,
    Vector2 AtlasTexelSize)
{
    public const int FloatCount = 8;
}

/// <summary>Serializes static-shadow constants in the HLSL declaration order.</summary>
public sealed class StaticSpriteShadowShaderConstantsUpdater
{
    public unsafe void Write(float* target, in StaticSpriteShadowSceneConstants constants)
    {
        target[0] = Math.Max(1.0f, constants.ViewportSize.X);
        target[1] = Math.Max(1.0f, constants.ViewportSize.Y);
        target[2] = Math.Clamp(constants.Opacity, 0.0f, 1.0f);
        target[3] = 0.0f;
        target[4] = constants.Projection.X;
        target[5] = constants.Projection.Y;
        target[6] = Math.Max(0.0f, constants.AtlasTexelSize.X);
        target[7] = Math.Max(0.0f, constants.AtlasTexelSize.Y);
    }
}
