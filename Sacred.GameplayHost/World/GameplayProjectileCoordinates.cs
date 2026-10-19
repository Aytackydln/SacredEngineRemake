using System.Numerics;
using Sacred.World.Geometry;

namespace Sacred.GameplayHost.World;

/// <summary>Original model-camera coordinates shared by projectile playback and scene consumers.</summary>
public static class GameplayProjectileCoordinates
{
    private static readonly Vector3 SceneScale = new(WorldModelPose.Scale * WorldModelPose.CameraProjection.M11,
        -WorldModelPose.Scale * WorldModelPose.CameraProjection.M22, WorldModelPose.Scale * WorldModelPose.CameraProjection.M33);
    public static Vector3 FromScene(Vector3 point) => point / SceneScale;
    public static Vector3 ToScene(Vector3 point) => point * SceneScale;
    public static Vector2 ToTiles(Vector3 point)
    {
        var scene = ToScene(point);
        return IsometricProjection.IsoToWorld(new(scene.X, -scene.Y / MathF.Sqrt(2)));
    }
}
