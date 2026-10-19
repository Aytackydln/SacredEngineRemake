using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.GameplayHost.World;
using Sacred.World.Geometry;
using Sacred.World.Objects;
using Sacred.World.Particles;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Graphics resources and input reach for gameplay-owned world objects.</summary>
internal sealed partial class DoorSceneController
{
    private const int MaximumConcurrentModelLoads = 2;
    private const float DoorClickRadiusTiles = 2;
    private readonly AssetManager _assets;
    private readonly SceneState _scene;
    private readonly Func<Vector2> _playerPosition;
    private readonly Dictionary<uint, Task<WorldModelAsset?>> _loads = [];
    private readonly HashSet<uint> _failedLoads = [];
    private readonly Dictionary<uint, SceneModel> _models = [];
    private readonly Dictionary<uint, WorldModelPlacement> _desiredModels = [];
    private readonly Dictionary<uint, DoorMotionPlayback> _doorAnimations = [];
    private readonly Dictionary<uint, (uint Type, long Version)> _instances = [];
    private readonly Dictionary<uint, bool> _animationStates = [];
    private readonly List<SceneModel> _orderedModels = [];
    public WorldDoorStateLayer States => _gameplay.DoorStates;

    public DoorSceneController(AssetManager assets, SceneState scene, IGameplayObjectWorld objects,
        Func<Vector2> playerPosition)
    {
        (_assets, _scene, _playerPosition) = (assets, scene, playerPosition);
        _offlineObjects = _gameplay = objects;
    }

    public void Update(VisibleWorld world, Vector2 focus, float deltaSeconds, IndoorTileGroup? activeIndoorGroup)
    {
        _gameplay.Update(world, activeIndoorGroup);
        if (_gameplayRevision != _gameplay.Revision)
        {
            _gameplayRevision = _gameplay.Revision;
            SelectGameplayModels();
        }
        if (world.LoadingSectors == 0) StartMissingLoads(focus);
        ApplyCompletedLoads();
        foreach (var pair in _models)
        {
            if (!_gameplay.Objects.TryGetValue(pair.Key, out var obj)) continue;
            var placement = obj.Placement;
            if (obj.Drop is { } drop)
            {
                var heightScale = WorldModelPose.Scale * WorldModelPose.CameraProjection.M33;
                pair.Value.SetModelProjection(DroppedItemModelPose.CameraProjection);
                pair.Value.SetModelOrientation(drop.Orientation);
                pair.Value.SetPose(new(drop.Position, drop.Height * heightScale), Vector3.Zero,
                    drop.Position, drop.GroundHeight * heightScale);
            }
            else pair.Value.SetPose(obj.Position, new(0, 0, obj.RotationRadians), new(placement.TileWorldX, placement.TileWorldY));
            if (!_doorAnimations.TryGetValue(pair.Key, out var playback)) continue;
            if (!_animationStates.TryGetValue(pair.Key, out var previous)) playback.SetInitialState(obj.IsActivated);
            else if (previous != obj.IsActivated)
            {
                playback.SetState(obj.IsActivated);
                EngineLog.WriteLine($"Gameplay object animation: ref {pair.Key}, {(obj.IsActivated ? "activate" : "reset")}.");
            }
            _animationStates[pair.Key] = obj.IsActivated;
        }
        UpdateDoorAnimations(deltaSeconds);
    }

    public bool IsDoorAt(Vector2 worldPosition) => FindInteractiveAt(worldPosition) is not null;
    public IEnumerable<WorldObjectSparkle> DroppedItemSparkles
    {
        get
        {
            foreach (var (reference, model) in _models)
            {
                if (!_gameplay.Objects.TryGetValue(reference, out var obj) || obj.Drop is null ||
                    (!obj.Definition.ModelDesc.EmitsWorldSparkles &&
                     ((obj.Replica?.BaseState.Flags ?? 0) & 0x80000) == 0)) continue;
                // Native 0x44B8CC centers the draw on the current world bounding box.
                // The model center already includes the flight orientation and height.
                var center = model.VisualCenter;
                var ground = IsometricProjection.IsoToWorld(new(center.X, -center.Y / MathF.Sqrt(2)));
                yield return new(reference, ground,
                    center.Z / (WorldModelPose.Scale * WorldModelPose.CameraProjection.M33));
            }
        }
    }
    public IEnumerable<(uint Id, Vector2 Position, DoorMotionPlayback Playback)> DebugDoors =>
        _doorAnimations.Where(pair => _desiredModels.ContainsKey(pair.Key)).Select(pair =>
            (pair.Key, WorldModelPose.TilePosition(_desiredModels[pair.Key].StaticObject), pair.Value));
    public IEnumerable<(uint Id, uint Trigger, Vector2 Position, bool Open, bool Locked)> DebugStates =>
        _desiredModels.Values.Where(p => p.StaticObject.DoorTriggerId.HasValue).Select(p =>
            (p.StaticObject.StaticId, p.StaticObject.DoorTriggerId!.Value, WorldModelPose.TilePosition(p.StaticObject),
                States.IsOpen(p.StaticObject.DoorTriggerId.Value), States.IsLocked(p.StaticObject.DoorTriggerId.Value)));
    public bool HasPendingLoads => _loads.Count != 0 || _desiredModels.Keys.Any(id => !_models.ContainsKey(id) && !_failedLoads.Contains(id));
    public bool TryToggleAt(Vector2 worldPosition) => FindInteractiveAt(worldPosition) is { } target && TryToggle(target);
    private bool TryToggle(WorldModelPlacement target) => TryRequestGameplayInteraction(target);

    private void SelectGameplayModels()
    {
        _desiredModels.Clear();
        var removed = false;
        foreach (var obj in _gameplay.Objects.Values.Where(obj => (obj.IsModelProp || obj.Drop is not null) && (!_gameplay.IsNetwork || obj.Placement.HeightLevelId <= 1)))
        {
            var identity = (obj.Placement.TypeId, obj.InstanceVersion);
            if (_instances.TryGetValue(obj.Reference, out var before) && before != identity)
            {
                removed |= _models.Remove(obj.Reference);
                _doorAnimations.Remove(obj.Reference); _animationStates.Remove(obj.Reference);
                _loads.Remove(obj.Reference); _failedLoads.Remove(obj.Reference);
            }
            _instances[obj.Reference] = identity;
            _desiredModels[obj.Reference] = new(obj.Placement, obj.Definition, obj.IsActivated ? DoorState.Open : DoorState.Closed);
        }
        foreach (var reference in _instances.Keys.Where(id => !_desiredModels.ContainsKey(id)).ToArray())
        {
            _instances.Remove(reference); _loads.Remove(reference); _failedLoads.Remove(reference);
            _doorAnimations.Remove(reference); _animationStates.Remove(reference);
            removed |= _models.Remove(reference);
        }
        if (removed) PublishModels();
    }

    private void StartMissingLoads(Vector2 focus)
    {
        var available = MaximumConcurrentModelLoads - _loads.Count;
        foreach (var (id, placement) in _desiredModels.OrderBy(pair => Vector2.DistanceSquared(WorldModelPose.TilePosition(pair.Value.StaticObject), focus)))
        {
            if (available <= 0) break;
            if (_models.ContainsKey(id) || _loads.ContainsKey(id) || _failedLoads.Contains(id)) continue;
            EngineLog.WriteLine($"World model request: item {placement.Item.ItemIndex} ({placement.Item.ModelName}) at {placement.StaticObject.TileWorldX},{placement.StaticObject.TileWorldY}.");
            _loads.Add(id, LoadWorldModelAsync(placement)); available--;
        }
    }

    private void ApplyCompletedLoads()
    {
        var changed = false;
        foreach (var (id, load) in _loads.Where(pair => pair.Value.IsCompleted).ToArray())
        {
            _loads.Remove(id);
            if (!_desiredModels.TryGetValue(id, out var placement)) continue;
            if (load.Status != TaskStatus.RanToCompletion || load.GetAwaiter().GetResult() is not { Model.Mesh: { } mesh } asset)
            { _failedLoads.Add(id); continue; }
            var model = WorldSceneModelFactory.Create(placement.StaticObject, placement.Item, mesh, asset.TextureAliases,
                asset.Model.Diagnostics?.SourceOriginOffset ?? Vector3.Zero, asset.Effects);
            _models[id] = model;
            if (asset.OpenAnimation is not null || asset.CloseAnimation is not null)
            {
                var animation = new DoorMotionPlayback(mesh, asset.Model.Skin, asset.OpenAnimation, asset.CloseAnimation);
                _doorAnimations[id] = animation;
                animation.SetInitialState(NetworkObjectOpen(id));
                model.SetMesh(animation.Mesh);
                if (animation.AnimatedMesh is { } animated) model.SetAnimatedMesh(animated);
            }
            EngineLog.WriteLine($"World model ready: item {placement.Item.ItemIndex} ({placement.Item.ModelName}) at {placement.StaticObject.TileWorldX},{placement.StaticObject.TileWorldY}.");
            changed = true;
        }
        if (changed) PublishModels();
    }

    private async Task<WorldModelAsset?> LoadWorldModelAsync(WorldModelPlacement placement)
    {
        try
        {
            if (_gameplay.Objects.TryGetValue(placement.StaticObject.StaticId, out var obj) && obj.Drop is not null)
                return await _assets.LoadDroppedItemModelAsync(obj).ConfigureAwait(false);
            return await _assets.LoadWorldModelAsync(placement.StaticObject.TypeId).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            EngineLog.WriteLine($"World model failed: static {placement.StaticObject.StaticId}, item {placement.Item.ItemIndex}: {error.Message}");
            return null;
        }
    }
    private void PublishModels()
    {
        _orderedModels.Clear();
        _orderedModels.AddRange(_models.OrderBy(pair => pair.Key).Select(pair => pair.Value));
        _scene.SetWorldModels(_orderedModels);
        EngineLog.WriteLine($"World models visible: {_orderedModels.Count}, loading: {_loads.Count}.");
    }
    private void UpdateDoorAnimations(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        foreach (var (id, animation) in _doorAnimations)
        {
            animation.Update(deltaSeconds);
            if (animation.AnimatedMesh is { } animated) _models[id].SetAnimatedMesh(animated);
            else _models[id].SetMesh(animation.Mesh);
        }
    }
    private WorldModelPlacement? FindInteractiveAt(Vector2 position) => _desiredModels.Values
        .Where(placement => placement.Item.ModelDesc.IsSelectable &&
            Vector2.DistanceSquared(WorldModelPose.TilePosition(placement.StaticObject), position) <= DoorClickRadiusTiles * DoorClickRadiusTiles)
        .OrderBy(placement => Vector2.DistanceSquared(WorldModelPose.TilePosition(placement.StaticObject), position))
        .Select(placement => (WorldModelPlacement?)placement).FirstOrDefault();
    private enum DoorState { Closed, Open }
    private readonly record struct WorldModelPlacement(StaticWorldObject StaticObject, ItemsPakEntry Item, DoorState State);
}
