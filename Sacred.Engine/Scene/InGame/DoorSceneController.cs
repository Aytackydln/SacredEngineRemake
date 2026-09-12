using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Granny.Meshes;

namespace Sacred.Engine.Scene.InGame;

/// <summary>
/// Maintains model-backed world-object families for streamed sectors and their local door state.
/// </summary>
internal sealed class DoorSceneController
{
    private const float ModelScale = 2.0f;
    private const int MaximumConcurrentModelLoads = 2;
    private const float DoorClickRadiusTiles = 2.0f;

    private readonly AssetManager _assets;
    private readonly SceneState _scene;
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

    public DoorSceneController(AssetManager assets, SceneState scene)
    {
        _assets = assets;
        _scene = scene;
    }

    public void Update(VisibleWorld world, Vector2 focus, float deltaSeconds)
    {
        if (VisibleSectorsChanged(world.Sectors))
        {
            SelectVisibleModels(world.Sectors);
        }

        // LoadingSectors includes work outside the visible snapshot.  A complete
        // 3x3 sector set is the reliable point at which the initial world-object
        // request order is stable.
        if (world.Sectors.Count == 9)
            StartMissingLoads(focus);
        ApplyCompletedLoads();
        UpdateDoorAnimations(deltaSeconds);
    }

    public bool IsDoorAt(Vector2 worldPosition) => FindInteractiveAt(worldPosition) is not null;

    public bool TryToggleAt(Vector2 worldPosition)
    {
        var placement = FindInteractiveAt(worldPosition);
        if (placement is not { } target)
            return false;

        if (!_doorAnimations.ContainsKey(target.StaticObject.StaticId))
            return false;

        var wasOpen = GetDoorState(target.StaticObject.StaticId) == DoorState.Open;
        if (wasOpen && !CanClose(target))
            return true;

        var nextState = wasOpen ? DoorState.Closed : DoorState.Open;
        _doorStates[target.StaticObject.StaticId] = nextState;

        if (_doorAnimations.TryGetValue(target.StaticObject.StaticId, out var animation))
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

    private void SelectVisibleModels(IReadOnlyList<Sector> sectors)
    {
        _desiredModels.Clear();
        var placementKeys = new HashSet<ModelPlacementKey>();
        // Script-created interactive objects are the gameplay instances. A matching WLDX
        // entry is their scenery representation and must not create a second model.
        foreach (var sector in sectors)
        foreach (var worldObject in sector.WorldObjects.Objects)
            AddModelIfPresent(worldObject, placementKeys);
        foreach (var sector in sectors)
        foreach (var staticObject in sector.StaticObjects.Objects)
            AddModelIfPresent(staticObject, placementKeys);

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
        foreach (var staticId in _doorStates.Keys.Where(staticId => !_desiredModels.ContainsKey(staticId)).ToArray())
            _doorStates.Remove(staticId);

        // A load continues after a sector leaves view. Keep tracking it until it
        // completes so a later publication cannot enqueue duplicate work.

        if (removed)
            PublishModels();
    }

    private void AddModelIfPresent(StaticWorldObject staticObject, HashSet<ModelPlacementKey> placementKeys)
    {
        var item = _assets.GetItem(staticObject.TypeId);
        if (item is not { } value || !IsWorldModel(value) ||
            string.IsNullOrWhiteSpace(value.ModelName))
        {
            return;
        }

        var key = new ModelPlacementKey(staticObject.TypeId, staticObject.TileWorldX, staticObject.TileWorldY);
        if (!placementKeys.Add(key))
            return;

        var state = IsInteractive(value)
            ? GetDoorState(staticObject.StaticId)
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
                         new Vector2(pair.Value.StaticObject.TileWorldX, pair.Value.StaticObject.TileWorldY), focus)))
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
            var model = CreateSceneModel(placement, mesh, asset.TextureAliases);
            _models[staticId] = model;
            if (asset.OpenAnimation is not null || asset.CloseAnimation is not null)
            {
                var animation = new DoorMotionPlayback(model, asset.Model.Skin, asset.OpenAnimation, asset.CloseAnimation);
                _doorAnimations[staticId] = animation;
                animation.SetInitialState(GetDoorState(staticId) == DoorState.Open);
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

    private static SceneModel CreateSceneModel(
        WorldModelPlacement placement,
        Mesh mesh,
        IReadOnlyDictionary<string, ModelTextureReference> textureAliases)
    {
        // Script-created objects retain their absolute tile anchor in the static-object
        // record. ProjectedX/Y are sprite-space values and must never be inverted for a
        // model transform: doing so makes a distant object share the player's 3D area.
        var label = IsInteractive(placement.Item) ? "Interactive model" : "World object";
        return new SceneModel(
            $"{label}: static {placement.StaticObject.StaticId}, item {placement.Item.ItemIndex}, {placement.Item.ModelName}",
            mesh,
            ModelPosition(placement),
            new Vector3(0.0f, 0.0f, DegreesToRadians(-placement.Item.ModelDesc.Angle3D)),
            ModelScale,
            textureAliases);
    }

    private static float DegreesToRadians(float degrees) =>
        float.IsFinite(degrees) ? degrees * (MathF.PI / 180.0f) : 0.0f;

    private void UpdateDoorAnimations(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;

        foreach (var animation in _doorAnimations.Values)
            animation.Update(deltaSeconds);
    }

    private WorldModelPlacement? FindInteractiveAt(Vector2 worldPosition)
    {
        var maximumDistanceSquared = DoorClickRadiusTiles * DoorClickRadiusTiles;
        WorldModelPlacement? closest = null;
        foreach (var placement in _desiredModels.Values)
        {
            if (!IsInteractive(placement.Item))
                continue;

            var position = new Vector2(placement.StaticObject.TileWorldX, placement.StaticObject.TileWorldY);
            if (Vector2.DistanceSquared(position, worldPosition) > maximumDistanceSquared)
                continue;

            if (closest is null ||
                Vector2.DistanceSquared(position, worldPosition) < Vector2.DistanceSquared(
                    new Vector2(closest.Value.StaticObject.TileWorldX, closest.Value.StaticObject.TileWorldY), worldPosition))
            {
                closest = placement;
            }
        }

        return closest;
    }

    private DoorState GetDoorState(uint staticId) =>
        _doorStates.TryGetValue(staticId, out var state) ? state : DoorState.Closed;

    private static Vector3 ModelPosition(WorldModelPlacement placement) => new(
        placement.StaticObject.TileWorldX,
        placement.StaticObject.TileWorldY,
        0.0f);

    private static bool IsWorldModel(ItemsPakEntry item) =>
        item.ModelDesc.GraphicType.HasFlag(SacredItemGraphicType.Model) &&
        item.ModelDesc.Category is SacredItemCategory.WorldObject or
            SacredItemCategory.Container or
            SacredItemCategory.Door or
            SacredItemCategory.Effect;

    private static bool IsInteractive(ItemsPakEntry item) =>
        item.ModelDesc.Category is SacredItemCategory.Door or SacredItemCategory.Container or SacredItemCategory.WorldObject;

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
