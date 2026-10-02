using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.World;

internal static class SectorPreloadDirection
{
    public static SectorCoord Ahead(Vector2 position, Vector2 movement)
    {
        var localX = position.X / Sector.TileCount - MathF.Floor(position.X / Sector.TileCount);
        var localY = position.Y / Sector.TileCount - MathF.Floor(position.Y / Sector.TileCount);
        if (MathF.Abs(movement.X) >= MathF.Abs(movement.Y))
        {
            if (movement.X > 0 && localX >= 0.5f) return new SectorCoord(1, 0);
            if (movement.X < 0 && localX <= 0.5f) return new SectorCoord(-1, 0);
        }
        else
        {
            if (movement.Y > 0 && localY >= 0.5f) return new SectorCoord(0, 1);
            if (movement.Y < 0 && localY <= 0.5f) return new SectorCoord(0, -1);
        }
        return default;
    }
}
