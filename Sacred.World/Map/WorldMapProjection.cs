using System.Numerics;

namespace Sacred.World.Map;

/// <summary>Original map affine transform, with Underworld's three rows above Ancaria.</summary>
public static class WorldMapProjection
{
    private const float CalibrationMapWidth = 2048;
    private const float UnderworldHeight = 768;
    // Gold worldToMap at 0x6CB3B0, projection at 0x622620. The native
    // 48/24 basis cancels the 53.66563 world-units-per-tile conversion.
    private const float TileStepX = 48 * 0.007446f;
    private const float TileStepY = 24 * 0.014914f;
    private static readonly Vector2 CombinedOrigin = new(1018.7499389648438f, -186.9473876953125f);

    public static Vector2 WorldToMap(Vector2 worldPosition, int mapWidth, WorldMapKind kind = WorldMapKind.Ancaria)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        var combined = CombinedOrigin + new Vector2(
            (worldPosition.X - worldPosition.Y) * TileStepX,
            (worldPosition.X + worldPosition.Y) * TileStepY);
        if (kind == WorldMapKind.Ancaria) combined.Y -= UnderworldHeight;
        return combined * (mapWidth / CalibrationMapWidth);
    }

    public static Vector2 MapToWorld(Vector2 mapPosition, int mapWidth, WorldMapKind kind = WorldMapKind.Ancaria)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        var combined = mapPosition * (CalibrationMapWidth / mapWidth);
        if (kind == WorldMapKind.Ancaria) combined.Y += UnderworldHeight;
        var relative = combined - CombinedOrigin;
        var difference = relative.X / TileStepX;
        var sum = relative.Y / TileStepY;
        return new((sum + difference) * 0.5f, (sum - difference) * 0.5f);
    }

    public static WorldMapKind GetMap(Vector2 worldPosition) =>
        WorldToMap(worldPosition, 2048).Y < 0 ? WorldMapKind.Underworld : WorldMapKind.Ancaria;

    public static bool Contains(Vector2 mapPosition, WorldMapAtlas atlas) =>
        mapPosition.X >= 0 && mapPosition.X < atlas.Width && mapPosition.Y >= 0 && mapPosition.Y < atlas.Height;
}
