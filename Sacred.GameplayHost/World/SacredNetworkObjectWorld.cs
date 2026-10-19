using System.Collections.ObjectModel;
using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;
using Sacred.World.Geometry;
using Sacred.World.Networking;
using Sacred.World.Objects;

namespace Sacred.GameplayHost.World;

/// <summary>Creates gameplay instances from received state and owns their trigger/interaction boundary.</summary>
public sealed class SacredNetworkObjectWorld : IGameplayObjectWorld
{
    private readonly SacredNetworkWorldBinding _world;
    private readonly IGameplayResourceCatalog _resources;
    private readonly GameplayNetworkInteractions _interactions;
    private readonly Dictionary<uint, GameplayWorldObject> _objects = [];
    private readonly HashSet<SectorCoord> _visibleSectors = [];
    private long _replicaRevision = -1;
    private readonly GameplayDroppedItems _drops = new();
    public SacredNetworkObjectWorld(SacredNetworkWorldBinding world, IGameplayResourceCatalog resources,
        IGameplayNetworkTransport? transport, Action<string> log)
    {
        (_world, _resources) = (world, resources);
        _interactions = new(world, transport, log);
        Objects = new ReadOnlyDictionary<uint, GameplayWorldObject>(_objects);
    }
    public bool IsNetwork => true;
    public bool IsCurrentSession => _world.IsCurrentSession;
    public long Revision { get; private set; }
    public IReadOnlyDictionary<uint, GameplayWorldObject> Objects { get; }
    public IReadOnlyDictionary<uint, GoldWorldTriggerState> Triggers => _world.Replicas.WorldTriggers.Records;
    public WorldDoorStateLayer DoorStates => _world.DoorStates.States;
    public string InteractionStatus => _interactions.Status;

    public void Update(VisibleWorld visibleWorld, IndoorTileGroup? indoorGroup)
    {
        _interactions.Update();
        _world.Update();
        if (!IsCurrentSession)
        {
            if (_objects.Count != 0) { _objects.Clear(); _drops.Clear(); Revision++; }
            return;
        }
        var sectors = _world.VisibleWorld.Sectors.Select(sector => sector.Coord).ToArray();
        if (_replicaRevision == _world.Replicas.Revision && _visibleSectors.SetEquals(sectors)) return;
        _replicaRevision = _world.Replicas.Revision;
        _visibleSectors.Clear(); _visibleSectors.UnionWith(sectors);
        _objects.Clear();
        foreach (var obj in _world.WorldObjects)
            if (_resources.GetItem(obj.BaseState.ObjectType) is { } item)
            {
                var instance = GameplayObjectPlacement.FromReplica(obj, item, _world.Replicas);
                if (GameplayDroppedItems.IsDroppedItem(obj, item)) instance = instance with { Drop = new() };
                _objects[obj.BaseState.ObjectReference] = instance;
            }
        _drops.Synchronize(_objects, NativeHeight);
        Revision++;
    }
    private float NativeHeight(Vector2 position) => _world.OutdoorHeightAt(position) /
                                                    (WorldModelPose.Scale * WorldModelPose.CameraProjection.M33);
    public void Advance(float seconds)
    {
        if (!IsCurrentSession) return;
        _drops.Advance(_objects, NativeHeight, seconds);
    }
    public bool RequestInteraction(uint reference, GameplayInteractionContext context) =>
        _objects.TryGetValue(reference, out var obj) && obj.CanInteract && _interactions.Request(obj, context);
    public void Dispose() { _interactions.Dispose(); _objects.Clear(); _drops.Clear(); }
}
