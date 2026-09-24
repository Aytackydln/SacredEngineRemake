using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.World.Geometry;

/// <summary>Places authored world models in the same isometric plane as terrain sprites.</summary>
public static class WorldModelPose
{
    // Native world projection: 768 pixels over a 400-unit orthographic height.
    public const float Scale = 768.0f / 400.0f;

    // Sacred looks from (0,1200,600). Adapt that view to the engine's 45-degree
    // model camera after facing has been applied, without distorting terrain.
    public static Matrix4x4 CameraProjection { get; } = Matrix4x4.CreateScale(
        (1024.0f / 534.0f) / Scale, MathF.Sqrt(2.0f / 5.0f), MathF.Sqrt(8.0f / 5.0f));

    public static Matrix4x4 LocalTransform(float angleDegrees, Vector3 sourceOriginOffset) =>
        Matrix4x4.CreateTranslation(sourceOriginOffset) * Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateRotationZ(RotationRadians(angleDegrees)) * CameraProjection;

    public static Vector2 TilePosition(StaticWorldObject placement) =>
        placement.PreciseWorldPosition ?? new Vector2(placement.TileWorldX, placement.TileWorldY);

    /// <summary>
    /// The item angle describes a direction in the projected world plane. Model-space Y
    /// is foreshortened by Sacred's camera (sin(pitch) = 1/sqrt(5)).
    /// </summary>
    public static float RotationRadians(float angleDegrees)
    {
        if (!float.IsFinite(angleDegrees))
            return 0.0f;

        var angle = -angleDegrees * (MathF.PI / 180.0f);
        return MathF.Atan2(MathF.Sin(angle) * MathF.Sqrt(5.0f), MathF.Cos(angle));
    }
}
