using Sacred.Core.World;
using Sacred.Core.World.Pathing;

namespace Sacred.GameplayHost.Scripting;

public readonly record struct WorldScriptPositionOccupant(uint NextOnTile, bool IsCreature);

/// <summary>Gold 0x472AF0. The tile query must perform the native demand-load,
/// object-chain rebuild and script patches before calling this predicate.</summary>
public static class WorldScriptPositionValidity
{
    public static bool IsAllowed(WldxTileRecord? tile, Func<uint, WorldScriptPositionOccupant?> findObject)
    {
        ArgumentNullException.ThrowIfNull(findObject);
        if (tile is not { } value) return true;
        if ((value.PathFlags & WorldPathFlags.Indoor) != 0) return false;
        var head = value.WorldObjectChainHeadId;
        var reference = head;
        HashSet<uint>? visited = null;
        while (reference != 0)
        {
            if (findObject(reference) is not { } occupant || occupant.IsCreature) return false;
            reference = occupant.NextOnTile;
            if (reference == head) break;
            // Native terminates at zero or the original head. A malformed cycle
            // excluding that head hangs native; fail explicitly instead of accepting it.
            if (reference != 0 && !(visited ??= []).Add(reference))
                throw new InvalidDataException("Script position object chain cycles without returning to its head.");
        }
        return !value.Properties.BlocksMovement && (value.PathFlags & WorldPathFlags.RuntimeBlocked) == 0;
    }
}
