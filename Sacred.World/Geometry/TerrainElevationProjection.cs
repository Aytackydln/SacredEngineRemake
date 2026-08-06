using System.Numerics;

namespace Sacred.World.Geometry;

/// <summary>
/// Projects Sacred's terrain-height axis into screen space. The original game uses a
/// diagonal elevation axis: increasing height moves a point equally right and upward.
/// </summary>
public static class TerrainElevationProjection
{
    private const float SquareRootOfTwo = 1.4142135623730951f;

    /// <summary>The height samples are already measured in projected screen-space units.</summary>
    public static float HorizontalWorldOffset(float worldHeight) =>
        worldHeight;

    /// <summary>Compensates the model camera, whose Z axis projects by 1/sqrt(2).</summary>
    public static float ModelVerticalWorldOffset(float worldHeight) =>
        worldHeight * SquareRootOfTwo;

    public static Vector2 ScreenOffset(float worldHeight, float zoom)
    {
        var offset = worldHeight * zoom;
        return new Vector2(offset, -offset);
    }

    public static Vector2 RemoveScreenOffset(Vector2 screenPosition, float worldHeight, float zoom) =>
        screenPosition - ScreenOffset(worldHeight, zoom);
}
