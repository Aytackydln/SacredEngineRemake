using System.Numerics;

namespace Sacred.Shaders;

public readonly record struct SurfaceLightMapSceneConstants(
    Vector2 ViewportSize,
    float NightBlend)
{
    public const int FloatCount = 3;
}

public static class SurfaceLightMapShaderConstantsWriter
{
    public static unsafe void Write(float* target, in SurfaceLightMapSceneConstants constants)
    {
        target[0] = constants.ViewportSize.X;
        target[1] = constants.ViewportSize.Y;
        target[2] = Math.Clamp(constants.NightBlend, 0.0f, 1.0f);
    }
}
