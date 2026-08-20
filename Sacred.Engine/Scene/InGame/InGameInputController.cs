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
    private readonly WorldStreamer _worldStreamer;
    private readonly WorldCollisionResolver _collision;
    private readonly WorldElevationSampler _elevation;
    private readonly IndoorTraversalController _indoors;
    private readonly SceneState _scene;
    private readonly WorldLightingController _worldLighting;
    private readonly InGameMapInputController _mapInput;
    private readonly Action _updateWindowTitle;
    private readonly Func<int> _viewportWidth;
    private readonly Func<int> _viewportHeight;
    private readonly Action<bool> _setHandCursor;

    public InGameInputController(
        InputState input,
        GamepadInputSource gamepad,
        SacredCamera camera,
        ClickToMoveController clickToMove,
        PlayerCharacterController player,
        StairsTraversalController stairs,
        WorldStreamer worldStreamer,
        SceneState scene,
        WorldLightingController worldLighting,
        Action<GameSceneId> requestSwitch,
        Action updateWindowTitle,
        Func<int> viewportWidth,
        Func<int> viewportHeight,
        Action<bool> setHandCursor)
    {
        _input = input;
        _gamepad = gamepad;
        _camera = camera;
        _clickToMove = clickToMove;
        _player = player;
        _stairs = stairs;
        _worldStreamer = worldStreamer;
        _collision = new WorldCollisionResolver(worldStreamer, () => scene.Indoor.ActiveGroup);
        _elevation = new WorldElevationSampler(worldStreamer);
        _indoors = new IndoorTraversalController(worldStreamer, scene.Indoor);
        _scene = scene;
        _worldLighting = worldLighting;
        _mapInput = new InGameMapInputController(input, gamepad, scene.Minimap, requestSwitch);
        _updateWindowTitle = updateWindowTitle;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _setHandCursor = setHandCursor;
    }

    public void Update(float deltaSeconds)
    {
        _player.ApplyPendingAssets();

        if (_mapInput.Update(deltaSeconds))
            return;

        if (_input.ConsumeXButtonCyclePressed() ||
            _gamepad.WasPressed(GamepadButtons.B))
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
        if (isDefending)
        {
            _clickToMove.StopMoving();
            _input.DiscardPointerMovementEvents();
        }
        else
        {
            _clickToMove.Update(
                _input,
                _camera,
                _viewportWidth(),
                _viewportHeight(),
                _collision,
                _elevation,
                deltaSeconds);
        }

        if (_input.ConsumeRightMouseButtonPressed() || _gamepad.WasPressed(GamepadButtons.X))
            _player.PlayAttack();

        _camera.UpdateFromInput(_input, deltaSeconds, _collision);
        var surfaceLevel = _scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0;
        if (_stairs.Update(_camera, surfaceLevel, out var destinationSurfaceLevel))
        {
            _clickToMove.StopMoving();
            _indoors.Reset(_camera.WorldCenter, destinationSurfaceLevel);
        }
        else
        {
            _indoors.Update(_camera.WorldCenter);
        }
        var lightingFocus = _scene.Models.Count > 0
            ? _scene.Models[0].Position
            : new Vector3(_camera.WorldCenter, 0.0f);
        var zone = _scene.Indoor.ActiveGroup is null
            ? _worldStreamer.GetZone(_camera.WorldCenter)
            : WorldZone.Indoors;
        if (_worldLighting.Update(deltaSeconds, _scene.Lighting, lightingFocus, zone))
            _updateWindowTitle();
        var terrainHeight = _elevation.SampleHeightOrZero(_camera.WorldCenter);
        _scene.Debug.ActorTerrainHeight = terrainHeight;
        _player.UpdatePose(
            _camera.WorldCenter,
            _camera.CharacterFacingUnitVector,
            _camera.CameraSpeedUnitVector != Vector2.Zero,
            _input.IsWalkModifierDown,
            isDefending,
            terrainHeight,
            _camera.LocomotionAnimationSpeed,
            deltaSeconds);
        _worldStreamer.Update(_camera.WorldCenter);
        var mouseWorld = GameActorElevation.ScreenToWorldOnSurface(
            _camera,
            _elevation,
            _input.MousePosition,
            _viewportWidth(),
            _viewportHeight());
        _setHandCursor(_stairs.IsStairsAt(mouseWorld, _scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0));
    }

    public void OnActivated() => _mapInput.OnActivated();

    public void OnDeactivated() => _mapInput.OnDeactivated();

    public void Teleport(Vector2 destination)
    {
        _clickToMove.StopMoving();
        _camera.StopMoving();
        _worldStreamer.CenterOnSector(
            (int)MathF.Floor(destination.X / WorldStreamer.SectorTileCount),
            (int)MathF.Floor(destination.Y / WorldStreamer.SectorTileCount));
        _camera.CenterOnTile(destination.X, destination.Y);
        _indoors.Reset(destination);

        var terrainHeight = _elevation.SampleHeightOrZero(_camera.WorldCenter);
        _scene.Debug.ActorTerrainHeight = terrainHeight;
        _player.UpdatePose(
            _camera.WorldCenter,
            _camera.CharacterFacingUnitVector,
            false,
            false,
            false,
            terrainHeight,
            _camera.LocomotionAnimationSpeed,
            0.0f);
    }

    public void InitializeLocation(Vector2 location) => _indoors.Reset(location);

    private static string FormatVisibility(bool visible) => visible ? "visible" : "hidden";
}
