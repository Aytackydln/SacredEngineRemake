using System;
using System.Numerics;
using Sacred.Engine.Multiplayer;
using Sacred.Engine.Platform;
using Sacred.World;
using Sacred.World.Networking;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Uses offline navigation controls with owned native packet publication.</summary>
internal sealed class NetworkMovementInput
{
    private readonly SacredCamera _camera;
    private readonly SacredNetworkWorldBinding _world;
    private readonly WorldCollisionResolver _collision;
    private readonly WorldElevationSampler _elevation;
    private readonly ClickToMoveController _click = new();
    private readonly PostStairsMovementInputGate _gate = new();
    private readonly NetworkMovementPublisher _publisher;
    private readonly NetworkWorldObjectInput _objects;
    private Vector2 _lastReported;
    private uint? _lastFacingBits;
    public string Status => _publisher.Error is { } error ? "movement failed: " + error : $"interactive movement ready; {_publisher.SentCount} owned movement messages sent";

    public NetworkMovementInput(SacredCamera camera, SacredNetworkWorldBinding world, WorldStreamer streamer,
        GameServerClientController client, InputState input, DoorSceneController doors)
    {
        (_camera, _world) = (camera, world);
        _collision = new(streamer, () => null, world.DoorStates.States)
            { VisibleWorldProvider = () => world.VisibleWorld, SectorReadyProvider = world.CanNavigateSector };
        _elevation = new(streamer) { VisibleWorldProvider = () => world.VisibleWorld };
        _publisher = new(client, world);
        _objects = new(doors, camera, _click, _collision);
        _lastReported = world.LocalPosition;
        _gate.BlockUntilNewInput(input);
    }

    public void NavigateTo(Vector2 target)
    {
        if (_world.Replicas.Session.IsPaused == true) throw new InvalidOperationException("Navigation waits for the server to resume.");
        if (_world.LocalHero.Creature?.IsSleeping == true) throw new InvalidOperationException("Navigation waits for the native wake state.");
        _objects.CancelApproach(); _click.NavigateTo(_camera, _collision, target, CollisionCheatMode.Walk);
    }
    public void Stop() { _objects.CancelApproach(); _click.StopMoving(); _camera.StopMoving(); }
    public void Suspend() { _objects.ClearHover(); _click.StopMoving(); _camera.StopMoving(); }
    public bool HasObjectHover => _objects.HasHover;
    public Func<bool>? ControllerInteractionPressed { get; init; }
    public string InteractionStatus => _objects.Status;
    public bool Interact(uint reference) => _objects.Request(reference);

    public void Update(InputState input, int width, int height, Func<Vector2, Vector2> outputToViewport,
        float deltaSeconds, bool allowInput = true)
    {
        if (!_world.IsCurrentSession || _publisher.Error is not null || _world.Replicas.Session.IsPaused == true) return;
        var reportedFacing = _world.LocalHero.FacingBits ?? _world.LocalHero.BaseState.Word28;
        if (reportedFacing != _lastFacingBits)
            _camera.RotateToward(SacredNetworkFacing.ToTileDirection(reportedFacing));
        if (_world.LocalPosition != _lastReported)
        {
            var reported = _world.LocalPosition;
            _camera.CenterOnTile(reported.X, reported.Y);
            _click.StopMoving();
        }
        if (_world.LocalHero.Creature?.IsSleeping == true)
        {
            Stop(); _objects.ClearHover();
            _publisher.Update(_camera.WorldCenter, _camera.WorldCenter, _camera.CharacterFacingUnitVector, false, false, deltaSeconds);
            _lastReported = _world.LocalPosition; _lastFacingBits = reportedFacing;
            return;
        }
        var start = _camera.WorldCenter;
        var availability = _gate.Update(input);
        if (input.UiWantsKeyboard || !allowInput) availability = availability with { Keyboard = false, Controller = false };
        if (input.UiWantsMouse || !allowInput)
        {
            availability = availability with { Mouse = false };
            input.DiscardUiCapturedPointerEvents();
        }
        if (!allowInput) Suspend();
        _objects.Update(input, outputToViewport(input.MousePosition), width, height, availability,
            allowInput && !input.UiWantsKeyboard && !input.UiWantsMouse,
            allowInput && !input.UiWantsKeyboard && ControllerInteractionPressed?.Invoke() == true);
        _click.Update(input, _camera, width, height, outputToViewport, _collision, _elevation,
            CollisionCheatMode.Walk, _ => _objects.Click(), deltaSeconds, availability);
        _camera.UpdateFromInput(input, deltaSeconds, _collision, CollisionCheatMode.Walk, 1, availability);
        if (!_world.TryGetMovementPosition(_camera.WorldCenter, out _))
        {
            _camera.CenterOnTile(start.X, start.Y);
            Stop();
        }
        var moved = Vector2.DistanceSquared(start, _camera.WorldCenter) > 0.0000001f;
        var target = _camera.NavigationTarget ?? _camera.WorldCenter + _camera.CharacterFacingUnitVector * 2;
        target = _collision.ResolveMovement(_camera.WorldCenter, target);
        if (!_world.TryGetMovementPosition(target, out _)) target = _camera.WorldCenter;
        _publisher.Update(_camera.WorldCenter, target, _camera.CharacterFacingUnitVector, moved,
            input.IsWalkModifierDown, deltaSeconds);
        var position = _camera.WorldCenter;
        var hero = _world.LocalHero;
        var facingBits = _camera.CharacterFacingUnitVector == Vector2.Zero ? hero.FacingBits ?? hero.BaseState.Word28 :
            SacredNetworkFacing.FromTileDirection(_camera.CharacterFacingUnitVector);
        if (!_world.TryGetPosition(position, out var nativePosition))
            throw new InvalidOperationException("Owned hero has no loaded, confirmed terrain.");
        _world.Replicas.PredictLocalPosition(nativePosition, facingBits);
        _lastFacingBits = facingBits;
        _lastReported = _world.LocalPosition;
        _camera.SetSurfaceHeight(_elevation.SampleHeightOrZero(_camera.WorldCenter));
    }
}
