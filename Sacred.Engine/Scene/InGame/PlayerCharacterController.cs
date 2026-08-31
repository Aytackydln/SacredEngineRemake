using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Engine.Animation;
using Sacred.Engine.Assets;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
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
    private float _movementRotationZ = -MathF.PI * 0.25f;
    private uint _activeModelEntryId;
    private uint _requestedModelEntryId;
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
        RequestModel(_requestedModelEntryId);
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
        _animation?.Update(deltaSeconds, locomotionAnimationSpeed);
        if (_scene.Models.Count > 0)
            _scene.Models[0].SetPose(_position, BuildRotation(), worldCenter, GroundPlaneZ);
    }

    public void PlayAttack() => _animation?.PlayAttack();

    public void CycleModel()
    {
        var next = _requestedModelEntryId >= (uint)_assets.PlayerCharacterCount
            ? FirstModelSlotId
            : _requestedModelEntryId + 1;

        RequestModel(next);
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

    private void RequestModel(uint entryId)
    {
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
            request = new PlayerModelRequest(entryId, ++_requestVersion);
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
                    .LoadPlayerCharacterAsync(request.EntryId, cancellationToken)
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
                .LoadPlayerCharacterAnimationsAsync(request.EntryId, cancellationToken)
                .ConfigureAwait(false);
            if (animations is null)
                return;

            var animation = await Task.Run(
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
                    },
                    cancellationToken)
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
        _scene.Lighting.PlayerLightDiameter = _assets.PlayableCharacterLightRadius > 0.0f
            ? _assets.PlayableCharacterLightRadius * 2.0f
            : item is { } value
                ? value.ModelDesc.ModelExtent * 2.0f
                : 0.0f;
    }

    private void UpdatePosition(Vector2 worldPosition, TerrainElevationSample terrain)
    {
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

    private sealed class PlayerModelRequest(uint entryId, long requestVersion)
    {
        private int _cancellationDisposed;

        public uint EntryId { get; } = entryId;
        public long RequestVersion { get; } = requestVersion;
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
