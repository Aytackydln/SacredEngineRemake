using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.World.Map;

/// <summary>Projects Sacred's world-sector grid onto the authored Ancaria map.</summary>
public static class WorldMapProjection
{
    private const float CalibrationMapSize = 2048.0f;

    // The 2004 map uses one fixed affine step for each world-sector axis. It is
    // not the 16x8 screen-space isometric projection used to draw the terrain.
    // These values are fitted against original-game world-map screenshots at
    // six positions spanning Ancaria. The screenshots render the authored map
    // at 80%, so their marked positions are registered back to its 2048px grid.
    private const float MapOriginX = 1018.5140f;
    private const float MapOriginY = -958.0883f;
    private static readonly Vector2 SectorXAxis = new(22.841148f, 22.913492f);
    private static readonly Vector2 SectorYAxis = new(-22.86053f, 22.94859f);
    private static readonly float SectorAxisDeterminant =
        SectorXAxis.X * SectorYAxis.Y - SectorYAxis.X * SectorXAxis.Y;

    public static Vector2 WorldToMap(Vector2 worldPosition, int mapWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        var sectorPosition = worldPosition / Sector.TileCount;
        var calibratedMapPosition =
            new Vector2(MapOriginX, MapOriginY) +
            SectorXAxis * sectorPosition.X +
            SectorYAxis * sectorPosition.Y;
        return calibratedMapPosition * (mapWidth / CalibrationMapSize);
    }

    public static Vector2 MapToWorld(Vector2 mapPosition, int mapWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        var calibratedMapPosition = mapPosition * (CalibrationMapSize / mapWidth);
        var relative = calibratedMapPosition - new Vector2(MapOriginX, MapOriginY);
        var sectorPosition = new Vector2(
            (relative.X * SectorYAxis.Y - SectorYAxis.X * relative.Y) /
            SectorAxisDeterminant,
            (SectorXAxis.X * relative.Y - relative.X * SectorXAxis.Y) /
            SectorAxisDeterminant);
        return sectorPosition * Sector.TileCount;
    }
}
