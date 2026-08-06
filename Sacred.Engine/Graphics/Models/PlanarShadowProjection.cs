using System;
using System.Numerics;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Builds the shader parameters for a bounded directional terrain shadow.</summary>
internal static class PlanarShadowProjection
{
    // The loaded Seraphim is approximately 75 units tall and rendered at 2x scene scale.
    // Keeping this as a renderer-scale limit avoids coupling generic rendering to a model ID.
    public const float MaximumLength = 150.0f;

    // Near-horizon shadows fade out in the lighting controller. Clamping the remaining
    // projection prevents enormous triangles during the short fade interval.
    private const float MinimumVerticalDirection = 0.18f;

    public static Vector4 CreateParameters(Vector3 directionToSun, float opacity)
    {
        var direction = directionToSun.LengthSquared() > float.Epsilon
            ? Vector3.Normalize(directionToSun)
            : Vector3.UnitZ;
        var vertical = MathF.Max(direction.Z, MinimumVerticalDirection);
        return new Vector4(
            direction.X / vertical,
            direction.Y / vertical,
            MaximumLength,
            Math.Clamp(opacity, 0.0f, 1.0f));
    }
}
