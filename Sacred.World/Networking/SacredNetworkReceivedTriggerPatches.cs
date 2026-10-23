using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Live TriggerPatch2 marks outdoor and indoor session tiles. Terrain waits retain the whole cell.</summary>
internal sealed class SacredNetworkReceivedTriggerPatches(GoldTriggerPatchReplica state,
    SacredNetworkPatchTiles tiles, SacredNetworkPatchTerrain terrain)
{
    public bool Apply(IReadOnlyDictionary<ushort, Sector> sectors)
    {
        var changed = false;
        while (state.TryPeekTile(out var cell))
        {
            terrain.Begin(sectors);
            var edits = new List<(WorldPathingLayer Plane, int X, int Y)>();
            if (cell.Surface <= 1 && tiles.Resolve(terrain, cell.X, cell.Y, 0) is { } outdoor) edits.Add(outdoor);
            if (cell.Surface > 0 && tiles.ResolveFloor(terrain, cell.X, cell.Y, unchecked((ushort)cell.Surface)) is { } indoor) edits.Add(indoor);
            if (!terrain.IsReady) break;
            terrain.Synchronize();
            foreach (var edit in edits)
            {
                edit.Plane[edit.X, edit.Y] = edit.Plane[edit.X, edit.Y] with { Flags = edit.Plane[edit.X, edit.Y].Flags | WorldPathFlags.Trigger };
                changed = true;
            }
            state.CompleteTile(cell);
        }
        return changed;
    }
}
