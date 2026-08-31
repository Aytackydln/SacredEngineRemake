using System.Numerics;

namespace Sacred.World.Geometry;

/// <summary>
/// Projects Sacred's two authored terrain-height axes into screen space.
/// </summary>
public static class TerrainElevationProjection
{
    private const float SquareRootOfTwo = 1.4142135623730951f;

    /// <summary>Compensates the model camera, whose Z axis projects by 1/sqrt(2).</summary>
    public static float ModelVerticalWorldOffset(float worldHeight) =>
        worldHeight * SquareRootOfTwo;

    public static Vector2 ScreenOffset(float worldHeight, float horizontalWorldOffset, float zoom) =>
        new(horizontalWorldOffset * zoom, -worldHeight * zoom);

    public static Vector2 RemoveScreenOffset(
        Vector2 screenPosition,
        float worldHeight,
        float horizontalWorldOffset,
        float zoom) =>
        screenPosition - ScreenOffset(worldHeight, horizontalWorldOffset, zoom);

    public static float HorizontalWorldOffset(float horizontalWorldOffset) => horizontalWorldOffset;
}
