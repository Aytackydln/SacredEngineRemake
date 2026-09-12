using System.Numerics;

namespace Sacred.Particles;

/// <summary>Native orthographic/view transform recovered from Sacred.exe.
/// Scales refer to the 1024 by 768 reference viewport, before remake camera zoom.</summary>
public sealed record SacredParticleProjection(float HorizontalScale, float VerticalScale,
    float GroundDepthFactor, float HeightFactor, float DefaultWind)
{
    public Vector2 Project(Vector3 displacement) => new(
        displacement.X * HorizontalScale,
        (displacement.Y * GroundDepthFactor - displacement.Z * HeightFactor) * VerticalScale);
}
