using System.Numerics;

namespace Sacred.World.Geometry;

/// <summary>Native source vertices/orientation followed by adaptation from Gold's +Y camera
/// to the scene's -Y camera. Reflect after rotation, including during three-axis flight.</summary>
public static class DroppedItemModelPose
{
    public static Matrix4x4 CameraProjection { get; } =
        Matrix4x4.CreateScale(1, -1, 1) * WorldModelPose.CameraProjection;
}
