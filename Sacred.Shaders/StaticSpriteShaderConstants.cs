using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Shaders;

[StructLayout(LayoutKind.Sequential)]
public readonly struct StaticSpriteInstance(
    float x,
    float y,
    float width,
    float height,
    float depth,
    uint textureIndex,
    uint frameCount,
    uint flags,
    float animationPeriodSeconds,
    float alphaLeft,
    float alphaTop,
    float alphaRight,
    float alphaBottom,
    uint atlasColumns,
    uint atlasRows)
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float Width = width;
    public readonly float Height = height;
    public readonly float Depth = depth;
    public readonly uint TextureIndex = textureIndex;
    public readonly uint FrameCount = frameCount;
    public readonly uint Flags = flags;
    public readonly float AnimationPeriodSeconds = animationPeriodSeconds;
    public readonly float AlphaLeft = alphaLeft;
    public readonly float AlphaTop = alphaTop;
    public readonly float AlphaRight = alphaRight;
    public readonly float AlphaBottom = alphaBottom;
    public readonly uint AtlasColumns = atlasColumns;
    public readonly uint AtlasRows = atlasRows;
    public readonly float Padding = 0.0f;
}

public readonly record struct StaticSpriteSceneConstants(
    Vector2 ViewportSize,
    float AlphaCutoff,
    Vector3 AmbientColour,
    float ScenePaperWhiteNits,
    float UnlitWhiteNits,
    float AnimationTimeSeconds,
    int WorldLightCount,
    float NightBlend)
{
    public const int FloatCount = 11;
}

/// <summary>Serializes static-sprite scene constants in the HLSL declaration order.</summary>
public sealed class StaticSpriteShaderConstantsUpdater
{
    public unsafe void Write(float* target, in StaticSpriteSceneConstants constants)
    {
        target[0] = constants.ViewportSize.X;
        target[1] = constants.ViewportSize.Y;
        target[2] = Math.Max(0.0f, constants.AlphaCutoff);
        target[3] = Math.Max(0, constants.WorldLightCount);
        target[4] = Math.Max(0.0f, constants.AmbientColour.X);
        target[5] = Math.Max(0.0f, constants.AmbientColour.Y);
        target[6] = Math.Max(0.0f, constants.AmbientColour.Z);
        target[7] = Math.Max(0.0f, constants.ScenePaperWhiteNits);
        target[8] = Math.Max(0.0f, constants.UnlitWhiteNits);
        target[9] = Math.Max(0.0f, constants.AnimationTimeSeconds);
        target[10] = Math.Clamp(constants.NightBlend, 0.0f, 1.0f);
    }
}
