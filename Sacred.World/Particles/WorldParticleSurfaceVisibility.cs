using Sacred.Core.World.Sector;

namespace Sacred.World.Particles;

internal static class WorldParticleSurfaceVisibility
{
    public static bool IsVisible(WorldParticleScriptPlacement placement,
        IReadOnlyList<IndoorTileGroup> groups, IndoorTileGroup? activeGroup)
    {
        // When both operands exist, the tile operand owns the building floor;
        // the world operand is the visual particle-system pivot.
        var x = placement.Creation.TilePosition?.X ?? (int)MathF.Floor(placement.WorldX);
        var y = placement.Creation.TilePosition?.Y ?? (int)MathF.Floor(placement.WorldY);
        var level = Math.Max(1, placement.Creation.TilePosition?.Z ??
                               placement.Creation.WorldPosition?.Z ?? 0);
        var belongsToBuilding = false;
        var hasMatchingLevel = false;
        var activeContains = false;
        var activeMatchesLevel = false;
        foreach (var group in groups)
        {
            if (!group.TryGetAuthoredLocalTile(x, y, out _, out _))
                continue;
            belongsToBuilding = true;
            hasMatchingLevel |= group.SurfaceLevel == level;
            if (group.Id == activeGroup?.Id)
            {
                activeContains = true;
                activeMatchesLevel = group.SurfaceLevel == level;
            }
        }
        return !belongsToBuilding || (hasMatchingLevel ? activeMatchesLevel : activeContains);
    }
}
