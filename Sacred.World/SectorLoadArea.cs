using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>Selects a sector square using the player's quarter of the current sector.</summary>
internal readonly record struct SectorLoadArea(SectorCoord First, SectorCoord Last)
{
    public static SectorCoord Quarter(Vector2 position)
    {
        var sectorX = position.X / Sector.TileCount;
        var sectorY = position.Y / Sector.TileCount;
        return new SectorCoord(
            sectorX - MathF.Floor(sectorX) < 0.5f ? -1 : 1,
            sectorY - MathF.Floor(sectorY) < 0.5f ? -1 : 1);
    }

    public static SectorLoadArea Create(SectorCoord center, SectorCoord quarter, SectorLoadMode mode) =>
        mode == SectorLoadMode.Four
            ? new(new SectorCoord(center.X + Math.Min(quarter.X, 0), center.Y + Math.Min(quarter.Y, 0)),
                new SectorCoord(center.X + Math.Max(quarter.X, 0), center.Y + Math.Max(quarter.Y, 0)))
            : new(new SectorCoord(center.X - 1, center.Y - 1), new SectorCoord(center.X + 1, center.Y + 1));

    public bool Contains(SectorCoord coord) =>
        coord.X >= First.X && coord.X <= Last.X && coord.Y >= First.Y && coord.Y <= Last.Y;
}
