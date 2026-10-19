using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;
using Sacred.World;
using Sacred.World.Objects;
using Sacred.World.Rendering;

namespace Sacred.GameplayHost.World;

/// <summary>Authored object instances and local trigger interactions, without graphics or input dependencies.</summary>
public sealed class SacredOfflineObjectWorld : IGameplayObjectWorld
{
    private readonly IGameplayResourceCatalog _resources;
    private readonly WorldDoorStateLayer _doorStates;
    private readonly Dictionary<uint, GameplayWorldObject> _objects = [];
    private readonly Dictionary<uint, bool> _activated = [];
    private VisibleWorld? _visible;
    private Sector[] _sectors = [];
    private IndoorTileGroupId? _indoor;
    private long _doorsRevision = -1;
    public bool IsNetwork => false;
    public bool IsCurrentSession => true;
    public long Revision { get; private set; }
    public SacredOfflineObjectWorld(IGameplayResourceCatalog resources, WorldDoorStateLayer doorStates)
    {
        (_resources, _doorStates) = (resources, doorStates);
        Objects = new ReadOnlyDictionary<uint, GameplayWorldObject>(_objects);
    }
    public static IGameplayObjectWorld Create(IGameplayResourceCatalog resources, SacredWorldArchive archive) =>
        new SacredOfflineObjectWorld(resources, new WorldDoorStateLayer(archive.ObjectScript.Doors.Values));
    public IReadOnlyDictionary<uint, GameplayWorldObject> Objects { get; }
    public IReadOnlyDictionary<uint, GoldWorldTriggerState> Triggers =>
        FrozenDictionary<uint, GoldWorldTriggerState>.Empty;
    public WorldDoorStateLayer DoorStates => _doorStates;
    public string InteractionStatus { get; private set; } = "offline";

    public void Update(VisibleWorld visibleWorld, IndoorTileGroup? indoorGroup)
    {
        if (_visible is not null && _sectors.SequenceEqual(visibleWorld.Sectors) && _indoor == indoorGroup?.Id && _doorsRevision == _doorStates.Revision) return;
        _visible = visibleWorld; _indoor = indoorGroup?.Id; _doorsRevision = _doorStates.Revision;
        _sectors = visibleWorld.Sectors.ToArray();
        _objects.Clear();
        var keys = new HashSet<(uint, int, int)>();
        var groups = visibleWorld.Sectors.SelectMany(sector => sector.IndoorTileGroups.Groups).DistinctBy(group => group.Id).ToArray();
        foreach (var placement in visibleWorld.Sectors.SelectMany(sector => sector.WorldObjects.Objects).OrderByDescending(obj => obj.PreciseWorldPosition.HasValue)
                     .Concat(visibleWorld.Sectors.SelectMany(sector => sector.StaticObjects.Objects)))
        {
            if (_resources.GetItem(placement.TypeId) is not { } item || !GameplayObjectPlacement.IsModelProp(item) ||
                !WorldObjectSurfaceVisibility.IsModelVisible(placement, groups, indoorGroup, item.ModelDesc.Category) ||
                !keys.Add((placement.TypeId, placement.TileWorldX, placement.TileWorldY))) continue;
            var activated = placement.DoorTriggerId is { } trigger ? _doorStates.IsOpen(trigger) : _activated.GetValueOrDefault(placement.StaticId);
            _objects[placement.StaticId] = GameplayObjectPlacement.FromAuthored(placement, item, activated);
        }
        Revision++;
    }

    public bool RequestInteraction(uint reference, GameplayInteractionContext context)
    {
        if (!_objects.TryGetValue(reference, out var obj) || !obj.Definition.ModelDesc.IsSelectable || !context.InReach) return false;
        if (obj.IsActivated ? !context.CanReset : !context.CanActivate)
        { InteractionStatus = "original motion is unavailable"; return true; }
        if (obj.Definition.ModelDesc.Category == SacredItemCategory.Door && obj.Placement.DoorTriggerId is null)
        { InteractionStatus = "door has no authored trigger"; return true; }
        if (obj.Placement.DoorTriggerId is { } trigger)
        {
            if (_doorStates.IsLocked(trigger)) { InteractionStatus = "door is locked"; return true; }
            if (obj.IsActivated && _doorStates.WouldOverlap(trigger, context.ActorPosition, WorldCollisionResolver.CharacterRadius, context.Surface)) return true;
            if (!_doorStates.TrySetOpen(trigger, !obj.IsActivated)) return true;
        }
        else
        {
            _activated[reference] = !obj.IsActivated;
        }
        _visible = null;
        InteractionStatus = $"object {reference}, {(obj.IsActivated ? "reset" : "activate")}";
        foreach (var pair in _objects.ToArray())
            if (pair.Key == reference || obj.Placement.DoorTriggerId is { } linked && pair.Value.Placement.DoorTriggerId == linked)
                _objects[pair.Key] = pair.Value with { IsActivated = !obj.IsActivated };
        Revision++;
        return true;
    }
    public void Dispose() { _objects.Clear(); _activated.Clear(); }
}
