using System.Numerics;

namespace Sacred.World.Geometry;

/// <summary>
/// Projects Sacred's two authored terrain-height axes into screen space.
/// </summary>
public static class TerrainElevationProjection
{
    private const float SquareRootOfTwo = 1.4142135623730951f;

    // Native height is a 3D world distance. Sacred's (0,1200,600) camera
    // projects Z by cos(atan(.5)) at 768/400 screen pixels per world unit.
    public static float ScreenHeight(float worldHeight) =>
        worldHeight * WorldModelPose.Scale * (2.0f / MathF.Sqrt(5.0f));

    /// <summary>Compensates the model camera, whose Z axis projects by 1/sqrt(2).</summary>
    public static float ModelVerticalWorldOffset(float worldHeight) =>
        ScreenHeight(worldHeight) * SquareRootOfTwo;

    public static Vector2 ScreenOffset(float worldHeight, float horizontalWorldOffset, float zoom) =>
        new(horizontalWorldOffset * zoom, -ScreenHeight(worldHeight) * zoom);

    public static Vector2 RemoveScreenOffset(
        Vector2 screenPosition,
        float worldHeight,
        float horizontalWorldOffset,
        float zoom) =>
        screenPosition - ScreenOffset(worldHeight, horizontalWorldOffset, zoom);

    public static float HorizontalWorldOffset(float horizontalWorldOffset) => horizontalWorldOffset;
}
