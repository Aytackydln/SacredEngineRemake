using System.Numerics;
using Sacred.Core.World.Elevation;
using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>
/// Samples Sacred's continuous terrain surface from the four signed WLDX corner heights.
/// The original game scales samples by 2.5 and fans each tile into four triangles around
/// the average center height.
/// </summary>
public sealed class WorldElevationSampler(WorldStreamer worldStreamer)
{
    public const float WorldHeightPerSample = 2.5f;

    private readonly Dictionary<SectorCoord, Sector> _sectors = new(capacity: 9);
    private VisibleWorld? _cachedWorld;

    public TerrainElevationSample SampleOrZero(Vector2 worldPosition) =>
        TrySample(worldPosition, out var sample) ? sample : default;

    public float SampleHeightOrZero(Vector2 worldPosition) =>
        SampleOrZero(worldPosition).Height;

    public bool TrySampleHeight(Vector2 worldPosition, out float height)
    {
        var found = TrySample(worldPosition, out var sample);
        height = sample.Height;
        return found;
    }

    public bool TrySample(Vector2 worldPosition, out TerrainElevationSample sample)
    {
        var tileX = (int)MathF.Floor(worldPosition.X);
        var tileY = (int)MathF.Floor(worldPosition.Y);
        if (!TryGetTile(tileX, tileY, out var sector, out var localX, out var localY))
        {
            sample = default;
            return false;
        }

        var fractionX = worldPosition.X - tileX;
        var fractionY = worldPosition.Y - tileY;
        var elevation = sector.Elevation[localX, localY];
        var height = SampleTile(elevation, fractionX, fractionY) * WorldHeightPerSample;
        sample = new TerrainElevationSample(height, 0.0f);
        return true;
    }

    internal static float SampleTile(TerrainElevationTile tile, float x, float y)
        => SampleValues(
            tile.SouthWest,
            tile.NorthWest,
            tile.NorthEast,
            tile.SouthEast,
            x,
            y);

    internal static float SampleValues(
        float southWest,
        float northWest,
        float northEast,
        float southEast,
        float x,
        float y)
    {
        x = Math.Clamp(x, 0.0f, 1.0f);
        y = Math.Clamp(y, 0.0f, 1.0f);

        var center = (southWest + northWest + northEast + southEast) * 0.25f;

        var diagonalX = x - y;
        var diagonalY = x + y - 1.0f;
        if (diagonalX > 0.0f)
        {
            return diagonalY > 0.0f
                ? InterpolateRight(northEast, southEast, center, x, y)
                : InterpolateNorth(northWest, northEast, center, x, y);
        }

        return diagonalY > 0.0f
            ? InterpolateSouth(southWest, southEast, center, x, y)
            : InterpolateLeft(southWest, northWest, center, x, y);
    }

    private bool TryGetTile(
        int worldTileX,
        int worldTileY,
        out Sector sector,
        out int localX,
        out int localY)
    {
        RefreshSectorIndex();
        var sectorCoord = new SectorCoord(
            FloorDiv(worldTileX, Sector.TileCount),
            FloorDiv(worldTileY, Sector.TileCount));
        if (!_sectors.TryGetValue(sectorCoord, out sector!))
        {
            localX = 0;
            localY = 0;
            return false;
        }

        localX = worldTileX - sectorCoord.X * Sector.TileCount;
        localY = worldTileY - sectorCoord.Y * Sector.TileCount;
        return true;
    }

    private void RefreshSectorIndex()
    {
        var visibleWorld = worldStreamer.VisibleWorld;
        if (ReferenceEquals(visibleWorld, _cachedWorld))
            return;

        _cachedWorld = visibleWorld;
        _sectors.Clear();
        foreach (var sector in visibleWorld.Sectors)
            _sectors[sector.Coord] = sector;
    }

    private static float InterpolateNorth(float northWest, float northEast, float center, float x, float y) =>
        northWest * (1.0f - x - y) + northEast * (x - y) + center * (2.0f * y);

    private static float InterpolateRight(float northEast, float southEast, float center, float x, float y) =>
        northEast * (x - y) + southEast * (x + y - 1.0f) + center * (2.0f * (1.0f - x));

    private static float InterpolateSouth(float southWest, float southEast, float center, float x, float y) =>
        southWest * (y - x) + southEast * (x + y - 1.0f) + center * (2.0f * (1.0f - y));

    private static float InterpolateLeft(float southWest, float northWest, float center, float x, float y) =>
        southWest * (y - x) + northWest * (1.0f - x - y) + center * (2.0f * x);

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }
}

public readonly record struct TerrainElevationSample(float Height, float HorizontalOffset);
