using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.World;
using Sacred.World.Geometry;
using Sacred.World.Objects;
using Sacred.World.Rendering;

namespace Sacred.Engine.Scene.InGame;

/// <summary>
/// Maintains model-backed world-object families for streamed sectors and their local door state.
/// </summary>
internal sealed partial class DoorSceneController
{
    private const int MaximumConcurrentModelLoads = 2;
    private const float DoorClickRadiusTiles = 2.0f;

    private readonly AssetManager _assets;
    private readonly SceneState _scene;
    private readonly Func<Vector2> _playerPosition;
    private readonly Dictionary<uint, Task<WorldModelAsset?>> _loads = [];
    private readonly HashSet<uint> _failedLoads = [];
    private readonly Dictionary<uint, SceneModel> _models = [];
    private readonly Dictionary<uint, WorldModelPlacement> _desiredModels = [];
    private readonly Dictionary<uint, DoorState> _doorStates = [];
    private readonly Dictionary<uint, DoorMotionPlayback> _doorAnimations = [];
    private readonly List<SceneModel> _orderedModels = [];

    // WorldStreamer republishes a VisibleWorld while sector loads complete.  The
    // wrapper instance is therefore not a useful change signal: the actual set
    // of loaded sectors is.  Re-selecting on every publication used to keep the
    // render thread busy enough that the first model requests never completed.
    private readonly HashSet<SectorCoord> _visibleSectorCoordinates = [];
    private IndoorTileGroupId? _activeIndoorGroupId;

    public WorldDoorStateLayer States { get; }

    public DoorSceneController(AssetManager assets, SceneState scene, WorldDoorStateLayer states,
        Func<Vector2> playerPosition)
    {
        _assets = assets;
        _scene = scene;
        States = states;
        _playerPosition = playerPosition;
    }

    public void Update(
        VisibleWorld world,
        Vector2 focus,
        float deltaSeconds,
        IndoorTileGroup? activeIndoorGroup)
    {
        var indoorGroupChanged = _activeIndoorGroupId != activeIndoorGroup?.Id;
        if (VisibleSectorsChanged(world.Sectors) || indoorGroupChanged)
        {
            _activeIndoorGroupId = activeIndoorGroup?.Id;
            SelectVisibleModels(world.Sectors, activeIndoorGroup);
        }

        // Wait for the selected sector group so the initial world-object request order is stable.
        if (world.LoadingSectors == 0 && world.Sectors.Count > 0)
            StartMissingLoads(focus);
        ApplyCompletedLoads();
        UpdateDoorAnimations(deltaSeconds);
    }

    public bool IsDoorAt(Vector2 worldPosition) => FindInteractiveAt(worldPosition) is not null;
    public IEnumerable<(uint Id, Vector2 Position, DoorMotionPlayback Playback)> DebugDoors =>
        _doorAnimations.Where(pair => _desiredModels.ContainsKey(pair.Key)).Select(pair =>
            (pair.Key, WorldModelPose.TilePosition(_desiredModels[pair.Key].StaticObject), pair.Value));
    public IEnumerable<(uint Id, uint Trigger, Vector2 Position, bool Open, bool Locked)> DebugStates =>
        _desiredModels.Values.Where(p => p.StaticObject.DoorTriggerId.HasValue).Select(p =>
            (p.StaticObject.StaticId, p.StaticObject.DoorTriggerId!.Value,
                WorldModelPose.TilePosition(p.StaticObject),
                States.IsOpen(p.StaticObject.DoorTriggerId.Value), States.IsLocked(p.StaticObject.DoorTriggerId.Value)));

    public bool HasPendingLoads
    {
        get
        {
            if (_loads.Count > 0) return true;
            foreach (var id in _desiredModels.Keys)
                if (!_models.ContainsKey(id) && !_failedLoads.Contains(id)) return true;
            return false;
        }
    }

    public bool TryToggleAt(Vector2 worldPosition)
    {
        var placement = FindInteractiveAt(worldPosition);
        if (placement is not { } target)
            return false;

        return TryToggle(target);
    }

    private bool TryToggle(WorldModelPlacement target)
    {

        if (!target.Item.ModelDesc.IsSelectable ||
            !_models.TryGetValue(target.StaticObject.StaticId, out var model) ||
            !WorldObjectInteractionReach.Contains(model, _playerPosition())) return false;

        if (target.Item.ModelDesc.Category == SacredItemCategory.Door &&
            (target.StaticObject.DoorTriggerId is not { } triggerId || States.IsLocked(triggerId)))
        {
            EngineLog.WriteLine($"Door locked: static {target.StaticObject.StaticId}; script state does not permit interaction.");
            return true;
        }

        if (!_doorAnimations.TryGetValue(target.StaticObject.StaticId, out var playback))
            return true;

        var wasOpen = GetDoorState(target.StaticObject) == DoorState.Open;
        if (!wasOpen && !playback.CanActivate) return true;
        if (wasOpen && !CanClose(target))
            return true;

        var nextState = wasOpen ? DoorState.Closed : DoorState.Open;
        _doorStates[target.StaticObject.StaticId] = nextState;

        if (target.StaticObject.DoorTriggerId is { } doorId)
        {
            if (wasOpen && States.WouldOverlap(doorId, _playerPosition(),
                WorldCollisionResolver.CharacterRadius, _scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0))
                return true;
            States.TrySetOpen(doorId, !wasOpen);
            foreach (var linked in _desiredModels.Values.Where(p => p.StaticObject.DoorTriggerId == doorId))
                if (_doorAnimations.TryGetValue(linked.StaticObject.StaticId, out var linkedAnimation))
                    linkedAnimation.SetState(!wasOpen);
        }

        else if (_doorAnimations.TryGetValue(target.StaticObject.StaticId, out var animation))
            animation.SetState(nextState == DoorState.Open);

        PublishModels();
        EngineLog.WriteLine(
            $"World model toggled: static {target.StaticObject.StaticId}, item {target.Item.ItemIndex}, {nextState}.");
        return true;
    }

    private bool VisibleSectorsChanged(IReadOnlyList<Sector> sectors)
    {
        if (_visibleSectorCoordinates.Count == sectors.Count &&
            sectors.All(sector => _visibleSectorCoordinates.Contains(sector.Coord)))
        {
            return false;
        }

        _visibleSectorCoordinates.Clear();
        foreach (var sector in sectors)
            _visibleSectorCoordinates.Add(sector.Coord);
        return true;
    }

    private void SelectVisibleModels(
        IReadOnlyList<Sector> sectors,
        IndoorTileGroup? activeIndoorGroup)
    {
        _desiredModels.Clear();
        var placementKeys = new HashSet<ModelPlacementKey>();
        var indoorGroups = sectors.SelectMany(static sector => sector.IndoorTileGroups.Groups)
            .DistinctBy(static group => group.Id).ToArray();
        // Script-created interactive objects are the gameplay instances. A matching WLDX
        // entry is their scenery representation and must not create a second model.
        foreach (var sector in sectors)
        foreach (var worldObject in sector.WorldObjects.Objects.OrderByDescending(obj => obj.PreciseWorldPosition.HasValue))
            AddModelIfPresent(worldObject, placementKeys, indoorGroups, activeIndoorGroup);
        foreach (var sector in sectors)
        foreach (var staticObject in sector.StaticObjects.Objects)
            AddModelIfPresent(staticObject, placementKeys, indoorGroups, activeIndoorGroup);

        EngineLog.WriteLine($"World model selection: {_desiredModels.Count} interactive objects in {sectors.Count} sectors.");

        var removed = false;
        foreach (var staticId in _models.Keys.Where(staticId => !_desiredModels.ContainsKey(staticId)).ToArray())
        {
            _models.Remove(staticId);
            removed = true;
        }
        foreach (var staticId in _doorAnimations.Keys.Where(staticId => !_desiredModels.ContainsKey(staticId)).ToArray())
            _doorAnimations.Remove(staticId);

        // A failed asset may be absent from a particular installation.  Do not
        // restart that same request every frame; retry if its sector later
        // leaves and re-enters the visible set.
        _failedLoads.RemoveWhere(staticId => !_desiredModels.ContainsKey(staticId));
        // Keep requested state when the sector leaves view, so reloading restores its pose.

        // A load continues after a sector leaves view. Keep tracking it until it
        // completes so a later publication cannot enqueue duplicate work.

        if (removed)
            PublishModels();
    }

    private void AddModelIfPresent(
        StaticWorldObject staticObject,
        HashSet<ModelPlacementKey> placementKeys,
        IReadOnlyList<IndoorTileGroup> indoorGroups,
        IndoorTileGroup? activeIndoorGroup)
    {
        var item = _assets.GetItem(staticObject.TypeId);
        if (item is not { } value || !IsWorldModel(value) ||
            string.IsNullOrWhiteSpace(value.ModelName) ||
            !WorldObjectSurfaceVisibility.IsModelVisible(
                staticObject,
                indoorGroups,
                activeIndoorGroup,
                value.ModelDesc.Category))
        {
            return;
        }

        var key = new ModelPlacementKey(staticObject.TypeId, staticObject.TileWorldX, staticObject.TileWorldY);
        if (!placementKeys.Add(key))
            return;

        var state = IsInteractive(value)
            ? GetDoorState(staticObject)
            : DoorState.Closed;
        _desiredModels[staticObject.StaticId] = new WorldModelPlacement(staticObject, value, state);
    }

    private void StartMissingLoads(Vector2 focus)
    {
        var availableLoads = MaximumConcurrentModelLoads - _loads.Count;
        if (availableLoads <= 0)
            return;

        foreach (var (staticId, placement) in _desiredModels
                     .OrderBy(pair => Vector2.DistanceSquared(
                         WorldModelPose.TilePosition(pair.Value.StaticObject), focus)))
        {
            if (availableLoads <= 0)
                break;

            if (_models.ContainsKey(staticId) || _loads.ContainsKey(staticId) || _failedLoads.Contains(staticId))
                continue;

            EngineLog.WriteLine(
                $"World model request: item {placement.Item.ItemIndex} ({placement.Item.ModelName}) at " +
                $"{placement.StaticObject.TileWorldX},{placement.StaticObject.TileWorldY}.");
            _loads.Add(staticId, LoadWorldModelAsync(placement));
            availableLoads--;
        }
    }

    private void ApplyCompletedLoads()
    {
        var changed = false;
        foreach (var (staticId, load) in _loads.Where(static pair => pair.Value.IsCompleted).ToArray())
        {
            _loads.Remove(staticId);
            if (!_desiredModels.ContainsKey(staticId))
                continue;

            if (load.Status != TaskStatus.RanToCompletion)
            {
                _failedLoads.Add(staticId);
                continue;
            }

            var asset = load.GetAwaiter().GetResult();
            if (asset?.Model.Mesh is not { } mesh)
            {
                _failedLoads.Add(staticId);
                continue;
            }

            var placement = _desiredModels[staticId];
            var model = WorldSceneModelFactory.Create(placement.StaticObject, placement.Item, mesh, asset.TextureAliases,
                asset.Model.Diagnostics?.SourceOriginOffset ?? Vector3.Zero);
            _models[staticId] = model;
            if (asset.OpenAnimation is not null || asset.CloseAnimation is not null)
            {
                var animation = new DoorMotionPlayback(mesh, asset.Model.Skin, asset.OpenAnimation, asset.CloseAnimation);
                _doorAnimations[staticId] = animation;
                animation.SetInitialState(GetDoorState(placement.StaticObject) == DoorState.Open);
                model.SetMesh(animation.Mesh);
                if (animation.AnimatedMesh is { } animatedMesh) model.SetAnimatedMesh(animatedMesh);
            }
            EngineLog.WriteLine(
                $"World model ready: item {placement.Item.ItemIndex} ({placement.Item.ModelName}) at " +
                $"{placement.StaticObject.TileWorldX},{placement.StaticObject.TileWorldY}.");
            changed = true;
        }

        if (changed)
            PublishModels();
    }

    private async Task<WorldModelAsset?> LoadWorldModelAsync(WorldModelPlacement placement)
    {
        try
        {
            var asset = await _assets.LoadWorldModelAsync(placement.StaticObject.TypeId).ConfigureAwait(false);
            return asset;
        }
        catch (Exception exception)
        {
            EngineLog.WriteLine($"World model failed: static {placement.StaticObject.StaticId}, item {placement.Item.ItemIndex}: {exception.Message}");
            return null;
        }
    }

    private void PublishModels()
    {
        _orderedModels.Clear();
        foreach (var pair in _models.OrderBy(static pair => pair.Key))
            _orderedModels.Add(pair.Value);
        _scene.SetWorldModels(_orderedModels);
        EngineLog.WriteLine($"World models visible: {_orderedModels.Count}, loading: {_loads.Count}.");
    }

    private void UpdateDoorAnimations(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;

        foreach (var (staticId, animation) in _doorAnimations)
        {
            animation.Update(deltaSeconds);
            if (animation.AnimatedMesh is { } animatedMesh)
                _models[staticId].SetAnimatedMesh(animatedMesh);
            else
                _models[staticId].SetMesh(animation.Mesh);

        }
    }

    private WorldModelPlacement? FindInteractiveAt(Vector2 worldPosition)
    {
        var maximumDistanceSquared = DoorClickRadiusTiles * DoorClickRadiusTiles;
        WorldModelPlacement? closest = null;
        foreach (var placement in _desiredModels.Values)
        {
            if (!IsInteractive(placement.Item))
                continue;

            var position = WorldModelPose.TilePosition(placement.StaticObject);
            if (Vector2.DistanceSquared(position, worldPosition) > maximumDistanceSquared)
                continue;

            if (closest is null ||
                Vector2.DistanceSquared(position, worldPosition) < Vector2.DistanceSquared(
                    WorldModelPose.TilePosition(closest.Value.StaticObject), worldPosition))
            {
                closest = placement;
            }
        }

        return closest;
    }

    private DoorState GetDoorState(StaticWorldObject placement) =>
        placement.DoorTriggerId is { } id
            ? States.IsOpen(id) ? DoorState.Open : DoorState.Closed
            : _doorStates.GetValueOrDefault(placement.StaticId, DoorState.Closed);

    private static Vector3 ModelPosition(WorldModelPlacement placement)
    {
        var position = WorldModelPose.TilePosition(placement.StaticObject);
        return new Vector3(position, 0.0f);
    }

    private static bool IsWorldModel(ItemsPakEntry item) =>
        item.ModelDesc.GraphicType.HasFlag(SacredItemGraphicType.Model) &&
        item.ModelDesc.Category is SacredItemCategory.WorldObject or
            SacredItemCategory.Container or
            SacredItemCategory.Door or
            SacredItemCategory.Effect;

    private static bool IsInteractive(ItemsPakEntry item) =>
        item.ModelDesc.IsSelectable;

    private bool CanClose(WorldModelPlacement placement) =>
        _doorAnimations.TryGetValue(placement.StaticObject.StaticId, out var animation) && animation.CanReset;

    private enum DoorState
    {
        Closed,
        Open
    }

    private readonly record struct WorldModelPlacement(
        StaticWorldObject StaticObject,
        ItemsPakEntry Item,
        DoorState State);

    private readonly record struct ModelPlacementKey(uint TypeId, int WorldX, int WorldY);
}

