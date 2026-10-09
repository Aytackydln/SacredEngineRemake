using System;
using System.Numerics;
using Windows.Gaming.Input;
using Sacred.Core.World.Sector;
using Sacred.Engine.Platform;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

internal sealed class InGameInputController
{
    private readonly InputState _input;
    private readonly GamepadInputSource _gamepad;
    private readonly SacredCamera _camera;
    private readonly ClickToMoveController _clickToMove;
    private readonly PlayerCharacterController _player;
    private readonly StairsTraversalController _stairs;
    private readonly DoorSceneController _doors;
    private readonly WorldStreamer _worldStreamer;
    private readonly WorldCollisionResolver _collision;
    private readonly WorldElevationSampler _elevation;
    private readonly IndoorTraversalController _indoors;
    private readonly IndoorStairsTraversalController _indoorStairs;
    private readonly SceneState _scene;
    private readonly WorldLightingController _worldLighting;
    private readonly InGameMapInputController _mapInput;
    private readonly Action _updateWindowTitle;
    private readonly Func<int> _viewportWidth;
    private readonly Func<int> _viewportHeight;
    private readonly Func<Vector2, Vector2> _outputToViewport;
    private readonly Action<bool> _setHandCursor;
    private readonly PostStairsMovementInputGate _postStairsMovementInput = new();
    private ElevationMovementTrace? _elevationTrace;
    internal WorldFocusController Focus { get; }
    private readonly WorldInteractionController _interaction;
    public PortalTraversalController? Portals { get; set; }

    public CollisionCheatMode CollisionMode { get; private set; }
    public float PlayerMovementSpeedMultiplier { get; private set; } = 1.0f;

    public void NavigateTo(Vector2 target)
    { _interaction.Cancel(); _clickToMove.NavigateTo(_camera, _collision, target, CollisionMode); }

    internal bool RequestInteraction() => _interaction.Request(Focus.Target, _indoors.SurfaceLevel, CollisionMode);

    internal void CancelInteraction()
    { _interaction.Cancel(); _clickToMove.StopMoving(); _camera.StopMoving(); }

    internal void SuspendGameplayInput()
    {
        CancelInteraction();
        Focus.Clear();
        _postStairsMovementInput.BlockUntilNewInput(_input);
    }

    public InGameInputController(
        InputState input,
        GamepadInputSource gamepad,
        SacredCamera camera,
        ClickToMoveController clickToMove,
        PlayerCharacterController player,
        StairsTraversalController stairs,
        DoorSceneController doors,
        WorldStreamer worldStreamer,
        SceneState scene,
        WorldLightingController worldLighting,
        Action<GameSceneId> requestSwitch,
        Action updateWindowTitle,
        Func<int> viewportWidth,
        Func<int> viewportHeight,
        Func<Vector2, Vector2> outputToViewport,
        Action<bool> setHandCursor)
    {
        _input = input;
        _gamepad = gamepad;
        _camera = camera;
        _clickToMove = clickToMove;
        _player = player;
        _stairs = stairs;
        _doors = doors;
        _worldStreamer = worldStreamer;
        _elevation = new WorldElevationSampler(worldStreamer)
        { ActiveIndoorGroup = () => scene.Indoor.ActiveGroup };
        _indoors = new IndoorTraversalController(worldStreamer, scene.Indoor);
        _indoorStairs = new IndoorStairsTraversalController(worldStreamer);
        _collision = new WorldCollisionResolver(worldStreamer, () => scene.Indoor.ActiveGroup, doors.States)
        { SurfaceLevelProvider = () => _indoors.SurfaceLevel };
        _scene = scene;
        _worldLighting = worldLighting;
        _mapInput = new InGameMapInputController(input, gamepad, scene.Minimap, requestSwitch);
        _updateWindowTitle = updateWindowTitle;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _outputToViewport = outputToViewport;
        _setHandCursor = setHandCursor;
        Focus = new WorldFocusController(doors, stairs)
        { IndoorStairs = _indoorStairs, ActiveIndoorGroup = () => scene.Indoor.ActiveGroup };
        _interaction = new WorldInteractionController(doors, stairs, camera, clickToMove, _collision)
        { IndoorStairs = _indoorStairs };
        _camera.ManualMovementAssist = intent => Focus.AssistManualMovement(_camera.WorldCenter, intent);
        _camera.ManualMovementSegment = (start, end) =>
        {
            _stairs.ObserveMovement(start, end, _indoors.SurfaceLevel);
            Portals?.ObserveMovement(start, end, _indoors.SurfaceLevel);
        };
    }

    public void Update(float deltaSeconds)
    {
        _player.ApplyPendingAssets();

        var uiWantsMouse = _input.UiWantsMouse;
        if (uiWantsMouse)
            _input.DiscardUiCapturedPointerEvents();

        if (_mapInput.Update(deltaSeconds))
            return;

        var movementInput = _postStairsMovementInput.Update(_input);
        UpdateFocus(movementInput);
        if (_camera.GetManualDirection(_input, movementInput) != Vector2.Zero || uiWantsMouse || _input.IsDefendDown)
            _interaction.Cancel();
        if (!_input.UiWantsKeyboard && (_input.ConsumePressed(VirtualKey.F) || _gamepad.WasPressed(GamepadButtons.A)) &&
            RequestInteraction())
        {
            EngineLog.WriteLine($"Debug input: interact; {Focus.Status}");
        }

        if (!uiWantsMouse && (_input.ConsumeXButtonCyclePressed() ||
            _gamepad.WasPressed(GamepadButtons.B)))
        {
            _player.CycleModel();
        }

        if (_input.ConsumePressed(VirtualKey.F7))
        {
            _worldLighting.CycleMode();
            _updateWindowTitle();
            EngineLog.WriteLine($"Debug input: world lighting {_worldLighting.Mode}");
        }

        if (_input.ConsumePressed(VirtualKey.F8))
        {
            _scene.Debug.StairsMapVisible = !_scene.Debug.StairsMapVisible;
            EngineLog.WriteLine($"Debug input: stairs and door tiles {FormatVisibility(_scene.Debug.StairsMapVisible)}");
        }

        if (_input.ConsumePressed(VirtualKey.F9))
        {
            _scene.Debug.BlockedAreasVisible = !_scene.Debug.BlockedAreasVisible;
            EngineLog.WriteLine($"Debug input: blocked tiles {FormatVisibility(_scene.Debug.BlockedAreasVisible)}");
        }

        var isDefending = _input.IsDefendDown;
        if (isDefending || uiWantsMouse)
        {
            _clickToMove.StopMoving();
            _input.DiscardPointerMovementEvents();
        }
        else
        {
            if (!movementInput.Mouse)
                _input.DiscardPointerMovementEvents();

            _clickToMove.Update(
                _input,
                _camera,
                _viewportWidth(),
                _viewportHeight(),
                _outputToViewport,
                _collision,
                _elevation,
                CollisionMode,
                _ => RequestInteraction(),
                deltaSeconds,
                movementInput);
        }

        if ((!uiWantsMouse && _input.ConsumeRightMouseButtonPressed()) || _gamepad.WasPressed(GamepadButtons.X))
            _player.PlayAttack();

        _camera.UpdateFromInput(
            _input,
            deltaSeconds,
            _collision,
            CollisionMode,
            PlayerMovementSpeedMultiplier,
            movementInput);
        _interaction.Update(_indoors.SurfaceLevel);
        var surfaceLevel = _indoors.SurfaceLevel;
        var portalDestinationLevel = surfaceLevel;
        var scriptTransition = Portals?.Update(_camera, surfaceLevel, out portalDestinationLevel) == true;
        if (scriptTransition)
        {
            _camera.DebugManualDirection = null;
            Focus.Clear();
            _interaction.Cancel();
            _clickToMove.StopMoving();
            _postStairsMovementInput.BlockUntilNewInput(_input);
            _indoors.Reset(_camera.WorldCenter, portalDestinationLevel);
            _indoorStairs.Reset();
            surfaceLevel = portalDestinationLevel;
            _stairs.Reset(_camera.WorldCenter, surfaceLevel);
        }
        var destinationSurfaceLevel = surfaceLevel;
        if (!scriptTransition &&
            (_stairs.Update(_camera, surfaceLevel, out destinationSurfaceLevel) ||
             _indoorStairs.Update(_camera, _scene.Indoor.ActiveGroup, out destinationSurfaceLevel)))
        {
            _camera.DebugManualDirection = null;
            Focus.Clear();
            _interaction.Cancel();
            _clickToMove.StopMoving();
            _postStairsMovementInput.BlockUntilNewInput(_input);
            _indoors.Reset(_camera.WorldCenter, destinationSurfaceLevel);
        }
        else
        {
            _indoors.Update(_camera.WorldCenter);
        }
        var zone = _scene.Indoor.ActiveGroup is null
            ? _worldStreamer.GetZone(_camera.WorldCenter)
            : WorldZone.Indoors;
        if (_worldLighting.Update(deltaSeconds, _scene.Lighting, zone))
            _updateWindowTitle();
        var terrain = _elevation.SampleOrZero(_camera.WorldCenter);
        _camera.SetSurfaceHeight(terrain.Height);
        _scene.Debug.ActorTerrainHeight = terrain.Height;
        _player.UpdatePose(
            _camera.WorldCenter,
            _camera.CharacterFacingUnitVector,
            _camera.CameraSpeedUnitVector != Vector2.Zero,
            _input.IsWalkModifierDown,
            isDefending,
            terrain,
            _camera.LocomotionAnimationSpeed,
            deltaSeconds);
        if (_elevationTrace?.Update(
                _camera,
                terrain,
                _elevation,
                deltaSeconds,
                _worldStreamer.VisibleWorld.LoadingSectors == 0) == true)
            _elevationTrace = null;
        _worldStreamer.Update(_camera.WorldCenter, _camera.CameraSpeedUnitVector);
        var mouseWorld = GameActorElevation.ScreenToWorldOnSurface(
            _camera,
            _elevation,
            _outputToViewport(_input.MousePosition),
            _viewportWidth(),
            _viewportHeight());
        _setHandCursor(!uiWantsMouse &&
            (Focus.Target is not null ||
              Portals?.IsPortalAt(mouseWorld, _indoors.SurfaceLevel) == true ||
              _stairs.IsStairsAt(mouseWorld, _indoors.SurfaceLevel)));
    }

    private void UpdateFocus(MovementInputAvailability availability)
    {
        var screen = Focus.DebugMouseScreen ?? _outputToViewport(_input.MousePosition);
        var world = GameActorElevation.ScreenToWorldOnSurface(_camera, _elevation, screen,
            _viewportWidth(), _viewportHeight());
        Focus.Update(_input, _camera, screen, world, _viewportWidth(), _viewportHeight(), _indoors.SurfaceLevel,
            _input.UiWantsKeyboard ? availability with { Keyboard = false } : availability);
    }

    public void OnActivated() => _mapInput.OnActivated();

    public void OnDeactivated() { _camera.DebugManualDirection = null; Focus.Clear(); _interaction.Cancel(); _mapInput.OnDeactivated(); }

    public void SetCollisionMode(CollisionCheatMode mode)
    {
        if (CollisionMode == mode)
            return;

        CollisionMode = mode;
        _interaction.Cancel();
        _clickToMove.StopMoving();
        _camera.StopMoving();
    }

    public void SetPlayerMovementSpeedMultiplier(float value) =>
        PlayerMovementSpeedMultiplier = float.IsFinite(value) ? Math.Clamp(value, 0.25f, 4.0f) : 1.0f;

    public void Teleport(Vector2 destination)
    {
        _camera.DebugManualDirection = null;
        Focus.Clear();
        _interaction.Cancel();
        Portals?.Reset(destination);
        _clickToMove.StopMoving();
        _camera.StopMoving();
        _worldStreamer.CenterOnSector(
            (int)MathF.Floor(destination.X / WorldStreamer.SectorTileCount),
            (int)MathF.Floor(destination.Y / WorldStreamer.SectorTileCount));
        _camera.CenterOnTile(destination.X, destination.Y);
        _indoors.Reset(destination);
        _indoorStairs.Reset();
        _stairs.Reset(destination, _scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0);

        var terrain = _elevation.SampleOrZero(_camera.WorldCenter);
        _camera.SetSurfaceHeight(terrain.Height);
        _scene.Debug.ActorTerrainHeight = terrain.Height;
        _player.UpdatePose(
            _camera.WorldCenter,
            _camera.CharacterFacingUnitVector,
            false,
            false,
            false,
            terrain,
            _camera.LocomotionAnimationSpeed,
            0.0f);
    }

    public bool TryStartElevationTrace(string route, out string message)
    {
        if (!ElevationMovementTrace.TryCreate(route, out var trace))
        {
            message = "unknown elevation route; use bellevue-a, bellevue-b, or shaddar";
            return false;
        }

        Teleport(trace.Start);
        _elevationTrace = trace;
        message = $"continuous elevation trace '{route}' queued at {trace.Start.X:0.##},{trace.Start.Y:0.##}";
        return true;
    }

    public void InitializeLocation(Vector2 location)
    {
        Portals?.Reset(location);
        _indoors.Reset(location);
    }

    private static string FormatVisibility(bool visible) => visible ? "visible" : "hidden";
}
