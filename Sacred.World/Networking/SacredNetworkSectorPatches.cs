using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Session-owned Gold 0x493440 pending-vector pass and 0x4AB160 registry.
/// The native receive branch publishes local event 10 after this pass; no wire packet is sent.</summary>
public sealed class SacredNetworkSectorPatches
{
    private readonly GoldMouseoverRegistry _registry;
    private readonly GoldScriptRegistrationReplica? _registrations;
    private readonly SacredNetworkPatchTiles _tiles;
    private readonly SacredNetworkPatchTerrain _terrain;
    private GoldTriggerPatchReplica? _triggerPatches;
    private SacredNetworkReceivedTriggerPatches? _receivedTriggers;
    internal void BindTriggerPatches(GoldTriggerPatchReplica state)
    {
        _triggerPatches = state; _receivedTriggers = new(state, _tiles, _terrain);
    }
    internal SacredNetworkSectorPatches(Func<uint, StaticObjectRecord?> readStatic, SacredNetworkPatchTerrain? terrain)
        : this(readStatic, terrain, null) { }
    internal SacredNetworkSectorPatches(Func<uint, StaticObjectRecord?> readStatic, SacredNetworkPatchTerrain? terrain,
        GoldScriptRegistrationReplica? registrations)
    {
        _registrations = registrations;
        _registry = registrations?.Mouseovers ?? new();
        _tiles = new(readStatic);
        _terrain = terrain ?? new();
        Active = _registry.Active;
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
        var patches = patchBank.Sections[5].Records.Select(record => GoldSectorScriptPatch.FromPayload(record.Data))
            .Concat(_registrations?.IncrementalMouseovers ?? []);
        foreach (var patch in patches)
        {
            if (patch.X / Sector.TileCount != sector.Coord.X || patch.Y / Sector.TileCount != sector.Coord.Y) continue;
            Plan(patch.X, patch.Y, patch.Surface, WorldPathFlags.Byte80);
            selected.Add(patch);
            // Registration's second mark uses the same zero-extended coordinates
            // and full surface word, so it is idempotent with the first mark.
        }
        var positions = positionBank.Sections[2].Records.Select(record => GoldSectorTriggerPosition.FromPayload(record.Data))
            .Concat(_triggerPatches?.IncrementalPositions ?? []);
        foreach (var position in positions)
        {
            if (position.X / Sector.TileCount != sector.Coord.X || position.Y / Sector.TileCount != sector.Coord.Y) continue;
            if (position.Surface <= 1) Plan(position.X, position.Y, 0, WorldPathFlags.Trigger);
            if (position.Surface > 0) Plan(position.X, position.Y, position.Surface, WorldPathFlags.Trigger);
        }
        if (!_terrain.IsReady) return false;
        _terrain.Synchronize();
        foreach (var edit in edits)
            edit.Plane[edit.X, edit.Y] = edit.Plane[edit.X, edit.Y] with { Flags = edit.Plane[edit.X, edit.Y].Flags | edit.Flag };
        foreach (var patch in selected)
            _registry.Register(patch);
        return true;
    }
    internal bool ApplyReceived(IReadOnlyDictionary<ushort, Sector> sectors, Func<ushort, SectorCoord?> coordinate)
    {
        var changed = _receivedTriggers?.Apply(sectors) ?? false;
        while (_registrations is not null && _registrations.TryPeekTile(out var work) && work is not null)
        {
            var patch = work.Patch;
            var target = new SectorCoord(patch.X / Sector.TileCount, patch.Y / Sector.TileCount);
            if (work.EnteredSectors.Any(id => coordinate(id) == target))
            {
                _terrain.Begin(sectors);
                var tile = _tiles.Resolve(_terrain, patch.X, patch.Y, patch.Surface);
                if (!_terrain.IsReady) break;
                _terrain.Synchronize();
                if (tile is { } edit)
                {
                    edit.Plane[edit.X, edit.Y] = edit.Plane[edit.X, edit.Y] with { Flags = edit.Plane[edit.X, edit.Y].Flags | WorldPathFlags.Byte80 };
                    changed = true;
                }
            }
            _registrations.CompleteTile(work);
        }
        return changed;
    }
    internal void NotifyEntered(ushort sector)
    {
        EnterNotificationCount++;
        SectorEntered?.Invoke(sector);
    }
}
