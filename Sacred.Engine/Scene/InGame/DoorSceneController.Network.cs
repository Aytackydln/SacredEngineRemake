using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Multiplayer;
using Sacred.World.Geometry;
using Sacred.World.Networking;
using Sacred.World.Objects;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class DoorSceneController
{
    private SacredNetworkWorldBinding? _networkWorld;
    private WorldDoorStateLayer? _offlineDoorStates;
    private NetworkDoorInteractionPublisher? _networkInteractions;
    private long _networkRevision = -1;
    private readonly Dictionary<uint, (uint Type, long Version)> _networkInstances = [];
    private readonly Dictionary<uint, bool> _networkOpenStates = [];
    public string NetworkInteractionStatus => _networkInteractions?.Status ?? "offline";

    public void BindNetworkWorld(SacredNetworkWorldBinding? world, GameServerClientController? client)
    {
        _networkInteractions?.Dispose();
        _offlineDoorStates ??= States;
        _networkWorld = world;
        _networkInteractions = world is not null && client is not null ? new(client, world) : null;
        States = world?.DoorStates.States ?? _offlineDoorStates;
        _networkRevision = -1;
        _networkInstances.Clear(); _networkOpenStates.Clear();
        _visibleSectorCoordinates.Clear(); _desiredModels.Clear();
        _models.Clear(); _doorAnimations.Clear(); _loads.Clear(); _failedLoads.Clear();
        PublishModels();
        EngineLog.WriteLine(world is null ? "World objects returned to offline state." : "World objects now follow received physical instances and triggers.");
    }

    private void UpdateNetworkObjects(Vector2 focus, float deltaSeconds)
    {
        var world = _networkWorld!;
        _networkInteractions?.Update();
        if (!world.IsCurrentSession) return;
        if (_networkRevision != world.Replicas.Revision)
        {
            _networkRevision = world.Replicas.Revision;
            SelectNetworkModels();
        }
        if (world.VisibleWorld.LoadingSectors == 0) StartMissingLoads(focus);
        ApplyCompletedLoads();
        foreach (var pair in _models)
        {
            if (!world.Replicas.Objects.TryGetValue(pair.Key, out var obj)) continue;
            var position = SacredNetworkPosition.ToTiles(obj.BaseState);
            var facing = BitConverter.UInt32BitsToSingle(obj.FacingBits ?? obj.BaseState.Word28);
            pair.Value.SetPose(new(position, 0), new(0, 0, WorldModelPose.RotationRadians(facing)),
                new((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y)));
            if (!_doorAnimations.TryGetValue(pair.Key, out var playback)) continue;
            var open = NetworkObjectOpen(pair.Key);
            if (!_networkOpenStates.TryGetValue(pair.Key, out var previous)) playback.SetInitialState(open);
            else if (previous != open)
            {
                playback.SetState(open);
                EngineLog.WriteLine($"Network object animation: ref {pair.Key}, {(open ? "activate" : "reset")}, trigger {obj.BaseState.TriggerReference}.");
            }
            _networkOpenStates[pair.Key] = open;
        }
        UpdateDoorAnimations(deltaSeconds);
    }

    private void SelectNetworkModels()
    {
        var world = _networkWorld!;
        _desiredModels.Clear();
        var removed = false;
        foreach (var obj in world.WorldObjects)
        {
            if (obj.Physical is null || obj.Creature is not null ||
                _assets.GetItem(obj.BaseState.ObjectType) is not { } item || !IsWorldModel(item) ||
                string.IsNullOrWhiteSpace(item.ModelName) || obj.BaseState.Surface > 1) continue;
            var state = obj.BaseState;
            var reference = state.ObjectReference;
            var identity = (state.ObjectType, obj.InstanceVersion);
            if (_networkInstances.TryGetValue(reference, out var previous) && previous != identity)
            {
                removed |= _models.Remove(reference);
                _doorAnimations.Remove(reference); _loads.Remove(reference);
                _networkOpenStates.Remove(reference); _failedLoads.Remove(reference);
            }
            _networkInstances[reference] = identity;
            var position = SacredNetworkPosition.ToTiles(state);
            var x = (int)MathF.Floor(position.X); var y = (int)MathF.Floor(position.Y);
            // This is a scene placement of a runtime reference, never a Static.pak lookup.
            var placement = new StaticWorldObject(reference, state.ObjectType, 0, state.Sector,
                0, 0, 0, 0, state.Surface, 0, 0, 0, 0, 0, x + y, y, x, 0, 0)
            { PreciseWorldPosition = position, ScriptSurfaceLevel = state.Surface };
            _desiredModels[reference] = new(placement, item, NetworkObjectOpen(reference) ? DoorState.Open : DoorState.Closed);
        }
        foreach (var reference in _networkInstances.Keys.Where(id => !_desiredModels.ContainsKey(id)).ToArray())
        {
            _networkInstances.Remove(reference); _networkOpenStates.Remove(reference); _loads.Remove(reference);
            _doorAnimations.Remove(reference); _failedLoads.Remove(reference);
            removed |= _models.Remove(reference);
        }
        if (removed) PublishModels();
    }

    private bool NetworkObjectOpen(uint reference)
    {
        if (_networkWorld is not { } world || !world.Replicas.Objects.TryGetValue(reference, out var obj)) return false;
        if (obj.BaseState.TriggerReference != 0 &&
            world.Replicas.WorldTriggers.Records.TryGetValue(obj.BaseState.TriggerReference, out var trigger))
        {
            if (obj.ActivationTriggerState == trigger.State && obj.Physical?.Word64 is 0xaa or 0xab)
                return obj.Physical.Word64 == 0xaa;
            return (trigger.State & 1) != 0;
        }
        return obj.Physical?.Word64 == 0xaa;
    }

    private bool TryRequestNetworkInteraction(WorldModelPlacement target)
    {
        if (!target.Item.ModelDesc.IsSelectable || !_models.TryGetValue(target.StaticObject.StaticId, out var model) ||
            !WorldObjectInteractionReach.Contains(model, _playerPosition())) return false;
        if (target.Item.ModelDesc.Category != SacredItemCategory.Door)
        {
            EngineLog.WriteLine($"Network interaction unavailable: {target.Item.ModelDesc.Category} activation is not implemented.");
            return true;
        }
        return _networkInteractions?.Request(target.StaticObject.StaticId) == true;
    }
}
