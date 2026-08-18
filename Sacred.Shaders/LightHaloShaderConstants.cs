using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Shaders;

[StructLayout(LayoutKind.Sequential)]
public readonly struct LightHaloInstance(
    float x,
    float y,
    float diameter,
    float opacity,
    Vector3 colour,
    uint shape)
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float Diameter = diameter;
    public readonly float Opacity = opacity;
    public readonly Vector3 Colour = colour;
    public readonly uint Shape = shape;
}

public readonly record struct LightHaloSceneConstants(
    Vector2 ViewportSize,
    float NightBlend,
    float WhiteNits,
    float AnimationTime)
{
    public const int FloatCount = 5;
}

public static class LightHaloShaderConstantsWriter
{
    public static unsafe void Write(float* target, in LightHaloSceneConstants constants)
    {
        target[0] = constants.ViewportSize.X;
        target[1] = constants.ViewportSize.Y;
        target[2] = Math.Clamp(constants.NightBlend, 0.0f, 1.0f);
        target[3] = Math.Max(0.0f, constants.WhiteNits);
        target[4] = Math.Max(0.0f, constants.AnimationTime);
    }
}
