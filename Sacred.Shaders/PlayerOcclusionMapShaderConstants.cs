using System.Numerics;

namespace Sacred.Shaders;

/// <summary>Constants for the screen-space map that controls static-object player occlusion.</summary>
public readonly record struct PlayerOcclusionMapSceneConstants(
    Vector2 ViewportSize,
    Vector2 PlayerScreenPosition,
    float RadiusPixels)
{
    public const int FloatCount = 5;
}

public static class PlayerOcclusionMapShaderConstantsWriter
{
    public static unsafe void Write(float* target, in PlayerOcclusionMapSceneConstants constants)
    {
        target[0] = constants.ViewportSize.X;
        target[1] = constants.ViewportSize.Y;
        target[2] = constants.PlayerScreenPosition.X;
        target[3] = constants.PlayerScreenPosition.Y;
        target[4] = Math.Max(0.0f, constants.RadiusPixels);
    }
}
