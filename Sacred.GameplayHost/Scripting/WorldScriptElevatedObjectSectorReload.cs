using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

public readonly record struct WorldScriptElevatedObjectLinks(uint Floor, uint NextOnTile, uint TileHead);

/// <summary>Compared 0x637AF0 elevated Container physical re-placement. The
/// caller resolves the original floor/indoor cell and inserts in reference order.
/// Base state, authored values and all other physical state remain unchanged.</summary>
public static class WorldScriptElevatedObjectSectorReload
{
    public static WorldScriptElevatedObjectLinks Reinsert(GoldObjectBaseState basis, uint previousFloor,
        WldxTileRecord tile, uint currentHead, uint floor)
    {
        if (basis.Flags != 0x201 || basis.Surface == 0 || floor == 0 ||
            (previousFloor != 0 && previousFloor != floor) || tile.Properties.Behavior != WldxTileBehavior.None)
            throw new NotSupportedException("Elevated Container reload requires its original ordinary indoor floor and absent/matching link.");
        return new(floor,currentHead != basis.ObjectReference ? currentHead : 0,basis.ObjectReference);
    }
}
