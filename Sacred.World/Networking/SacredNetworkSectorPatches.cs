using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Session-owned Gold 0x493440 pending-vector pass and 0x4AB160 registry.
/// The native receive branch publishes local event 10 after this pass; no wire packet is sent.</summary>
public sealed class SacredNetworkSectorPatches
{
    private readonly List<GoldSectorScriptPatch> _active = [];
    private readonly HashSet<(ushort X, ushort Y, ushort Surface)> _keys = [];
    private readonly SacredNetworkPatchTiles _tiles;
    private readonly SacredNetworkPatchTerrain _terrain;
    internal SacredNetworkSectorPatches(Func<uint, StaticObjectRecord?> readStatic, SacredNetworkPatchTerrain? terrain)
    {
        _tiles = new(readStatic);
        _terrain = terrain ?? new();
        Active = _active.AsReadOnly();
    }
    public IReadOnlyList<GoldSectorScriptPatch> Active { get; }
    public long EnterNotificationCount { get; private set; }
    public event Action<ushort>? SectorEntered;

    internal bool Apply(Sector sector, IReadOnlyDictionary<ushort, Sector> sectors,
        GoldWorldBankSnapshot patchBank, GoldWorldBankSnapshot positionBank)
    {
        _terrain.Begin(sectors);
        var selected = new List<GoldSectorScriptPatch>();
        var edits = new List<(WorldPathingLayer Plane, int X, int Y, WorldPathFlags Flag)>();
        void Plan(int x, int y, ushort surface, WorldPathFlags flag)
        {
            if (_tiles.Resolve(_terrain, x, y, surface) is { } tile) edits.Add((tile.Plane, tile.X, tile.Y, flag));
        }
        foreach (var record in patchBank.Sections[5].Records)
        {
            var patch = GoldSectorScriptPatch.FromPayload(record.Data);
            if (patch.X / Sector.TileCount != sector.Coord.X || patch.Y / Sector.TileCount != sector.Coord.Y) continue;
            Plan(patch.X, patch.Y, patch.Surface, WorldPathFlags.Byte80);
            selected.Add(patch);
            // Registration's second mark uses the same zero-extended coordinates
            // and full surface word, so it is idempotent with the first mark.
        }
        foreach (var record in positionBank.Sections[2].Records)
        {
            var position = GoldSectorTriggerPosition.FromPayload(record.Data);
            if (position.X / Sector.TileCount != sector.Coord.X || position.Y / Sector.TileCount != sector.Coord.Y) continue;
            if (position.Surface <= 1) Plan(position.X, position.Y, 0, WorldPathFlags.Trigger);
            if (position.Surface > 0) Plan(position.X, position.Y, position.Surface, WorldPathFlags.Trigger);
        }
        if (!_terrain.IsReady) return false;
        _terrain.Synchronize();
        foreach (var edit in edits)
            edit.Plane[edit.X, edit.Y] = edit.Plane[edit.X, edit.Y] with { Flags = edit.Plane[edit.X, edit.Y].Flags | edit.Flag };
        foreach (var patch in selected)
            if (_keys.Add((patch.X, patch.Y, patch.Surface)))
            {
                if (patch.InsertAtFront) _active.Insert(0, patch);
                else _active.Add(patch);
            }
        return true;
    }
    internal void NotifyEntered(ushort sector)
    {
        EnterNotificationCount++;
        SectorEntered?.Invoke(sector);
    }
}
