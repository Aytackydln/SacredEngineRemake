using System.Numerics;

namespace Sacred.World.Geometry;

/// <summary>Physical model depth in the same tile-diagonal units as depth-enabled sprite planes.</summary>
public static class WorldModelDepth
{
    // CameraProjection scales native Y by sqrt(2/5) and Z by sqrt(8/5).
    // Native camera depth is proportional to 2Y-Z, so adapted Z contributes
    // one quarter as much as adapted Y. The 45-degree render view is only
    // responsible for the screen projection, not the native depth direction.
    public static float FromPosition(Vector3 modelPosition) =>
        -(modelPosition.Y - modelPosition.Z * 0.25f) / (24f * MathF.Sqrt(2f));
}
