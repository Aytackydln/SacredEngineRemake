using System.Numerics;

namespace Sacred.World.Geometry;

/// <summary>
/// Produces X+Y depth-buffer keys for model occlusion. Static sprite submission
/// follows <see cref="WorldStaticDrawOrder"/>; floating-point depth keys must not
/// replace its exact diagonal, tile and linked-list ordering.
/// </summary>
public static class WorldPainterDepth
{
    public static float FromTile(int worldX, int worldY, int chainDepth = 0) =>
        worldX + worldY + worldY * 0.001f + worldX * 0.000001f + chainDepth * 0.0000001f;

    public static float FromWorld(Vector2 worldPosition) =>
        worldPosition.X + worldPosition.Y + worldPosition.Y * 0.001f;

}
