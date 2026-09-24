using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.Pak.Items;
using Sacred.Engine.Animation;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Inventory.Actors;
using Sacred.World;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Keeps player asset transitions and scene-instance mutation out of the frame orchestrator.</summary>
internal sealed class PlayerCharacterController : IDisposable
{
    private const uint FirstModelSlotId = 1;
    private const float SceneScale = 2.0f;

    private readonly object _requestGate = new();
    private readonly AssetManager _assets;
    private readonly SceneState _scene;
    private readonly Mesh _proxyMesh = MeshFactory.CreateHumanoidProxyMesh();
    private readonly HashSet<PlayerModelRequest> _requests = [];

    private Vector3 _position;
    private Vector2 _worldCenter;
    private float _movementRotationZ = -MathF.PI * 0.25f;
    private uint _activeModelEntryId;
    private uint _requestedModelEntryId;
    private PlayerCharacterLoadout _loadout;
    private PlayerCharacterAsset? _activeAsset;
    private CharacterAnimationState? _animation;
    private PlayerModelRequest? _currentRequest;
    private PendingPlayerModel? _pendingModel;
    private PendingPlayerAnimation? _pendingAnimation;
    private float _modelGroundOffset;
    private long _requestVersion;
    private bool _transitionPending;
    private bool _disposed;

    public PlayerCharacterController(
        AssetManager assets,
        SceneState scene,
        string? initialCharacterName)
    {
        _assets = assets;
        _scene = scene;
        _activeModelEntryId = TestCharacters.ResolveEntryId(initialCharacterName);
        _requestedModelEntryId = _activeModelEntryId;
        _loadout = _assets.CreatePlayerCharacterLoadout(_activeModelEntryId);
    }

    public string SelectedCharacterName => TestCharacters.GetDisplayName(_requestedModelEntryId);

    public void Initialize(Vector2 worldCenter)
    {
        UpdatePosition(worldCenter, default);
        UpdatePlayerLight(_activeModelEntryId);
        _scene.AddModel(new SceneModel(
            "Loading player model",
            _proxyMesh,
            _position,
            BuildRotation(),
            groundPlaneZ: GroundPlaneZ));
        RequestModel(_loadout);
    }

    public void ApplyPendingAssets()
    {
        ApplyPendingModel();
        ApplyPendingAnimation();
    }

    public void UpdatePose(
        Vector2 worldCenter,
        Vector2 facingDirection,
        bool isMoving,
        bool isWalking,
        bool isDefending,
        TerrainElevationSample terrain,
        float locomotionAnimationSpeed,
        float deltaSeconds)
    {
        var cameraWorldMovement = worldCenter - _worldCenter;
        UpdatePosition(worldCenter, terrain);
        if (facingDirection != Vector2.Zero)
        {
            var angleRadians = MathF.Atan2(facingDirection.Y, facingDirection.X);
            _movementRotationZ = -(angleRadians + MathF.PI / 4);
        }

        _animation?.SetLocomotionState(isDefending
            ? CharacterAnimationStateId.Defend
            : isMoving
                ? isWalking
                    ? CharacterAnimationStateId.Walk
                    : CharacterAnimationStateId.Run
                : CharacterAnimationStateId.Idle);
        if (_scene.Models.Count > 0)
            _scene.Models[0].SetPoseFollowingCamera(
                _position,
                BuildRotation(),
                worldCenter,
                GroundPlaneZ,
                cameraWorldMovement);
        _animation?.Update(deltaSeconds, locomotionAnimationSpeed);
    }

    public void PlayAttack() => _animation?.PlayAttack();

    public PlayerDebugPanelState CreateDebugPanelState()
    {
        var slots = new PlayerEquipmentSlotState[_loadout.Actor.EquipmentSlots.Count];
        var occurrences = new Dictionary<EquipmentSlotType, int>();
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = _loadout.Actor.EquipmentSlots[index];
            occurrences.TryGetValue(slot.Type, out var occurrence);
            occurrences[slot.Type] = occurrence + 1;
            var displayName = occurrence == 0 && _loadout.Actor.EquipmentSlots.Count(candidate => candidate.Type == slot.Type) == 1
                ? slot.Type.ToString()
                : $"{slot.Type} {occurrence + 1}";
            slots[index] = new PlayerEquipmentSlotState(
                index,
                displayName,
                slot.Equipment?.Name,
                slot.Equipment?.IdemId);
        }

        var itemSets = new PlayerItemSetState[_assets.ItemSets.Count];
        for (var index = 0; index < itemSets.Length; index++)
        {
            var set = _assets.ItemSets[index];
            if (!_assets.CanEquipItemSet(index, _loadout.Actor.CharacterClass))
                continue;

            itemSets[index] = new PlayerItemSetState(
                index,
                set.SetIdentifier,
                set.ItemIds.Count,
                _assets.ResolveItemSetEquipment(index).Count);
        }

        itemSets = itemSets.Where(static set => set.ResolvedEquipmentCount > 0).ToArray();

        var presets = new PlayerCharacterPresetState[_assets.PlayerCharacterCount];
        for (var index = 0; index < presets.Length; index++)
        {
            var entryId = checked((uint)index + 1);
            presets[index] = new PlayerCharacterPresetState(entryId, TestCharacters.GetDisplayName(entryId));
        }

        return new PlayerDebugPanelState(
            SelectedCharacterName,
            _requestedModelEntryId,
            presets,
            slots,
            itemSets);
    }

    public bool RemoveEquipment(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_loadout.Actor.EquipmentSlots.Count ||
            _loadout.Actor.EquipmentSlots[slotIndex].Equipment is null)
        {
            return false;
        }

        var slot = _loadout.Actor.EquipmentSlots[slotIndex];
        EngineLog.WriteLine($"Player equipment removed: {slot.Type} {slot.Equipment!.Value.Name}.");
        slot.Unequip();
        RequestModel(_loadout);
        return true;
    }

    public bool EquipItemSet(int setIndex)
    {
        if (!_assets.CanEquipItemSet(setIndex, _loadout.Actor.CharacterClass))
            return false;

        var equipment = _assets.ResolveItemSetEquipment(setIndex);
        if (equipment.Count == 0)
            return false;

        var equipped = _loadout.Actor.EquipSet(equipment);
        if (equipped == 0)
            return false;

        EngineLog.WriteLine($"Player item set equipped: set {setIndex} replaced {equipped} equipment slot(s).");
        RequestModel(_loadout);
        return true;
    }

    public bool SelectModel(uint entryId)
    {
        if (entryId < 1 || entryId > _assets.PlayerCharacterCount) return false;
        _loadout = _assets.CreatePlayerCharacterLoadout(entryId);
        RequestModel(_loadout);
        EngineLog.WriteLine($"Debug input: selected character {TestCharacters.GetDisplayName(entryId)}");
        return true;
    }

    public void CycleModel()
    {
        var next = _requestedModelEntryId >= (uint)_assets.PlayerCharacterCount
            ? FirstModelSlotId
            : _requestedModelEntryId + 1;

        _loadout = _assets.CreatePlayerCharacterLoadout(next);
        RequestModel(_loadout);
        EngineLog.WriteLine($"Debug input: selected character {TestCharacters.GetDisplayName(next)}");
    }

    public void Dispose()
    {
        PlayerModelRequest[] requests;
        lock (_requestGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            requests = [.. _requests];
            foreach (var request in requests)
                request.Cancellation.Cancel();
        }

        Task.WhenAll(Array.ConvertAll(requests, static request => request.Completion.Task))
            .GetAwaiter()
            .GetResult();
    }

    private void RequestModel(PlayerCharacterLoadout loadout)
    {
        var entryId = loadout.EntryId;
        PlayerModelRequest? supersededRequest;
        PlayerModelRequest request;
        lock (_requestGate)
        {
            if (_disposed)
                return;

            supersededRequest = _currentRequest;
            _requestedModelEntryId = entryId;
            _transitionPending = true;
            Interlocked.Exchange(ref _pendingModel, null);
            Interlocked.Exchange(ref _pendingAnimation, null);
            request = new PlayerModelRequest(entryId, ++_requestVersion, loadout.Snapshot());
            _currentRequest = request;
            _requests.Add(request);
        }

        if (supersededRequest is not null)
            supersededRequest.Cancellation.Cancel();

        StopAnimationForTransition();
        _ = RunModelRequestAsync(request);
    }

    private async Task RunModelRequestAsync(PlayerModelRequest request)
    {
        try
        {
            var cancellationToken = request.Cancellation.Token;
            PlayerCharacterAsset player;
            try
            {
                player = await _assets
                    .LoadPlayerCharacterAsync(request.Loadout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                lock (_requestGate)
                {
                    if (!_disposed && ReferenceEquals(_currentRequest, request))
                    {
                        _requestedModelEntryId = _activeModelEntryId;
                        _transitionPending = false;
                    }
                }

                Debug.WriteLine($"Player model slot {request.EntryId} failed to load: {exception}");
                return;
            }

            var loadAnimation = false;
            lock (_requestGate)
            {
                if (IsCurrentModelRequest(request))
                {
                    Interlocked.Exchange(
                        ref _pendingModel,
                        new PendingPlayerModel(request.EntryId, request.RequestVersion, player));
                    loadAnimation = true;
                }
            }

            if (loadAnimation)
                await LoadAnimationAsync(request, player, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_requestGate)
            {
                if (ReferenceEquals(_currentRequest, request))
                    _currentRequest = null;
                _requests.Remove(request);
            }

            request.Completion.TrySetResult();
            request.DisposeCancellation();
        }
    }

    private async Task LoadAnimationAsync(
        PlayerModelRequest request,
        PlayerCharacterAsset player,
        CancellationToken cancellationToken)
    {
        try
        {
            var animations = await _assets
                .LoadPlayerCharacterAnimationsAsync(player, cancellationToken)
                .ConfigureAwait(false);
            if (animations is null)
                return;

            var animation = await _assets.ScheduleVisiblePreparation(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var state = new CharacterAnimationState(
                            player.Model,
                            animations,
                            _proxyMesh,
                            player.EquipmentEffects);
                        cancellationToken.ThrowIfCancellationRequested();
                        return state;
                    })
                .ConfigureAwait(false);

            lock (_requestGate)
            {
                if (IsMatchingRequest(request))
                {
                    Interlocked.Exchange(
                        ref _pendingAnimation,
                        new PendingPlayerAnimation(request.EntryId, request.RequestVersion, animation));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Player animation for slot {request.EntryId} failed to load: {exception}");
        }
    }

    private bool IsCurrentModelRequest(PlayerModelRequest request) =>
        _transitionPending && IsMatchingRequest(request);

    private bool IsMatchingRequest(PlayerModelRequest request) =>
        !_disposed &&
        ReferenceEquals(_currentRequest, request) &&
        request.RequestVersion == _requestVersion &&
        request.EntryId == _requestedModelEntryId;

    private void StopAnimationForTransition()
    {
        _animation = null;
        if (_scene.Models.Count > 0 && _activeAsset?.Model.Mesh is { } baseMesh)
            _scene.SetModelMesh(0, baseMesh);
    }

    private void ApplyPendingModel()
    {
        var pending = Interlocked.Exchange(ref _pendingModel, null);
        if (pending is null ||
            pending.RequestVersion != _requestVersion ||
            pending.EntryId != _requestedModelEntryId)
        {
            return;
        }

        _activeModelEntryId = pending.EntryId;
        _requestedModelEntryId = pending.EntryId;
        _transitionPending = false;
        _animation = null;
        _activeAsset = pending.Player;
        UpdatePlayerLight(pending.EntryId);

        var player = pending.Player;
        EngineLog.WriteLine($"Player character loaded: {player.DisplayName}");
        _modelGroundOffset = CalculateModelGroundOffset(player.Model);
        var sceneModel = new SceneModel(
            $"{player.DisplayName}: item {player.ItemId}, {player.ModelName}",
            player.Model.Mesh ?? _proxyMesh,
            _position,
            BuildRotation(),
            SceneScale,
            player.TextureAliases,
            player.EquipmentEffects,
            GroundPlaneZ);

        if (_scene.Models.Count == 0)
            _scene.AddModel(sceneModel);
        else
            _scene.SetModel(0, sceneModel);
    }

    private void ApplyPendingAnimation()
    {
        var pending = Interlocked.Exchange(ref _pendingAnimation, null);
        if (pending is null ||
            pending.RequestVersion != _requestVersion ||
            pending.EntryId != _activeModelEntryId ||
            _scene.Models.Count == 0)
        {
            return;
        }

        _animation = pending.Animation;
        pending.Animation.ApplyEquipmentEffectPose();
        _scene.SetModelMesh(0, pending.Animation.Mesh);
    }

    private Vector3 BuildRotation() => new(0.0f, 0.0f, _movementRotationZ);

    private float GroundPlaneZ => _position.Z - _modelGroundOffset;

    private void UpdatePlayerLight(uint entryId)
    {
        var item = _assets.GetItem(entryId);
        // Playable actors use the largest authored invisible light volume from
        // Items.pak. Character ModelExtent is the model's spatial bound and was
        // producing a much smaller, class-dependent pool of light.
        var radius = _assets.PlayableCharacterLightRadius > 0.0f
            ? checked((ushort)_assets.PlayableCharacterLightRadius)
            : item is { } value
                ? value.ModelDesc.Radius
                : (ushort)0;
        var light = SacredSurfaceLightResolver.CreatePlayerAttributes(radius);
        _scene.Lighting.PlayerLightDiameter = light.Radius * 2.0f;
        _scene.Lighting.PlayerLightColour = new Vector3(
            light.Colour.Red / 255.0f,
            light.Colour.Green / 255.0f,
            light.Colour.Blue / 255.0f);
        _scene.Lighting.PlayerLightOpacity = light.Opacity;
    }

    private void UpdatePosition(Vector2 worldPosition, TerrainElevationSample terrain)
    {
        _worldCenter = worldPosition;
        _position = new Vector3(
            worldPosition.X + TerrainElevationProjection.HorizontalWorldOffset(
                terrain.HorizontalOffset),
            worldPosition.Y,
            TerrainElevationProjection.ModelVerticalWorldOffset(terrain.Height) + _modelGroundOffset);
    }

    private static float CalculateModelGroundOffset(GrnAsset model)
    {
        if (model.Diagnostics?.WholeModelBounds is not { } bounds)
            return 0.0f;

        return -bounds.Min.Z * SceneScale;
    }

    private sealed record PendingPlayerModel(
        uint EntryId,
        long RequestVersion,
        PlayerCharacterAsset Player);

    private sealed record PendingPlayerAnimation(
        uint EntryId,
        long RequestVersion,
        CharacterAnimationState Animation);

    private sealed class PlayerModelRequest(
        uint entryId,
        long requestVersion,
        PlayerCharacterLoadout loadout)
    {
        private int _cancellationDisposed;

        public uint EntryId { get; } = entryId;
        public long RequestVersion { get; } = requestVersion;
        public PlayerCharacterLoadout Loadout { get; } = loadout;
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void DisposeCancellation()
        {
            if (Interlocked.Exchange(ref _cancellationDisposed, 1) == 0)
                Cancellation.Dispose();
        }
    }
}
