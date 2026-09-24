using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;

namespace Sacred.World.Rendering;

/// <summary>Selects world-object visuals for the player's active building floor.</summary>
public static class WorldObjectSurfaceVisibility
{
    private const int ExteriorActiveLayer = 1;

    public static bool TryResolveStatic(
        StaticWorldObject staticObject,
        IndoorTileGroup? activeIndoorGroup,
        out bool isIndoorSurface)
    {
        isIndoorSurface = false;
        if (activeIndoorGroup is null)
            return staticObject.SurfaceRenderLayer <= ExteriorActiveLayer;

        var belongsToActiveBuilding = staticObject.IndoorAnchor is { } anchor &&
            activeIndoorGroup.TryGetAuthoredLocalTile(anchor.X, anchor.Y, out _, out _);
        if (staticObject.SurfaceRenderLayer > ExteriorActiveLayer)
        {
            isIndoorSurface = belongsToActiveBuilding &&
                              staticObject.SurfaceRenderLayer == activeIndoorGroup.SurfaceRenderLayer;
            return isIndoorSurface;
        }

        return !belongsToActiveBuilding ||
               staticObject.SurfaceRenderLayer != ExteriorActiveLayer ||
               !staticObject.UsesAlternateSurface;
    }

    public static bool IsModelVisible(
        StaticWorldObject worldObject,
        IReadOnlyList<IndoorTileGroup> indoorGroups,
        IndoorTileGroup? activeIndoorGroup,
        SacredItemCategory category)
    {
        if (worldObject.ScriptSurfaceLevel is not { } scriptSurfaceLevel)
            return TryResolveStatic(worldObject, activeIndoorGroup, out _);

        // Town entrance doors use Z zero even when their ordering anchor lies
        // inside a level-one grid. Other Z-zero models are ground-floor props.
        if (scriptSurfaceLevel == 0 && category == SacredItemCategory.Door)
            return true;

        var authoredSurfaceLevel = Math.Max(1, (int)scriptSurfaceLevel);
        var belongsToIndoorFloor = false;
        var hasAuthoredLevel = false;
        var belongsToActiveFloor = false;
        var belongsToActiveAuthoredLevel = false;
        foreach (var group in indoorGroups)
        {
            if (!group.TryGetAuthoredLocalTile(
                    worldObject.TileWorldX,
                    worldObject.TileWorldY,
                    out _,
                    out _))
                continue;

            belongsToIndoorFloor = true;
            var matchesAuthoredLevel = group.SurfaceLevel == authoredSurfaceLevel;
            hasAuthoredLevel |= matchesAuthoredLevel;
            if (group.Id == activeIndoorGroup?.Id)
            {
                belongsToActiveFloor = true;
                belongsToActiveAuthoredLevel = matchesAuthoredLevel;
            }
        }

        return !belongsToIndoorFloor ||
               (hasAuthoredLevel ? belongsToActiveAuthoredLevel : belongsToActiveFloor);
    }
}
