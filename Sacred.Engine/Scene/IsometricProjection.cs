using System;
using System.Numerics;

namespace Sacred.Engine.Scene;

public static class IsometricProjection
{
    public const int StepWidth = 96;
    public const int StepHeight = 48;

    private const float HalfStepWidth = StepWidth * 0.5f;
    private const float HalfStepHeight = StepHeight * 0.5f;

    public static Vector2 WorldToIso(float worldX, float worldY) =>
        new((worldX - worldY) * HalfStepWidth, (worldX + worldY) * HalfStepHeight);

    public static Vector2 WorldToIso(Vector2 world) => WorldToIso(world.X, world.Y);

    public static Vector2 IsoToWorld(Vector2 iso)
    {
        var difference = iso.X / HalfStepWidth;
        var sum = iso.Y / HalfStepHeight;
        return new Vector2((sum + difference) * 0.5f, (sum - difference) * 0.5f);
    }

    public static Vector2 ScreenToWorld(
        Vector2 screenPosition,
        Vector2 worldCenter,
        float zoom,
        int viewportWidth,
        int viewportHeight)
    {
        if (zoom <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(zoom));

        var screenCenter = new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f);
        var targetIso = WorldToIso(worldCenter) + (screenPosition - screenCenter) / zoom;
        return IsoToWorld(targetIso);
    }
}
