using System;
using Windows.Gaming.Input;
using Sacred.Engine.Platform;
using Sacred.Engine.World;

namespace Sacred.Engine.Scene.InGame;

internal sealed class InGameInputController
{
    private readonly InputState _input;
    private readonly GamepadInputSource _gamepad;
    private readonly SacredCamera _camera;
    private readonly ClickToMoveController _clickToMove;
    private readonly PlayerCharacterController _player;
    private readonly WorldStreamer _worldStreamer;
    private readonly SceneState _scene;
    private readonly WorldLightingController _worldLighting;
    private readonly Action<GameSceneId> _requestSwitch;
    private readonly Action _updateWindowTitle;
    private readonly Func<int> _viewportWidth;
    private readonly Func<int> _viewportHeight;

    public InGameInputController(
        InputState input,
        GamepadInputSource gamepad,
        SacredCamera camera,
        ClickToMoveController clickToMove,
        PlayerCharacterController player,
        WorldStreamer worldStreamer,
        SceneState scene,
        WorldLightingController worldLighting,
        Action<GameSceneId> requestSwitch,
        Action updateWindowTitle,
        Func<int> viewportWidth,
        Func<int> viewportHeight)
    {
        _input = input;
        _gamepad = gamepad;
        _camera = camera;
        _clickToMove = clickToMove;
        _player = player;
        _worldStreamer = worldStreamer;
        _scene = scene;
        _worldLighting = worldLighting;
        _requestSwitch = requestSwitch;
        _updateWindowTitle = updateWindowTitle;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
    }

    public void Update(float deltaSeconds)
    {
        _player.ApplyPendingAssets();

        if (_input.ConsumePressed(VirtualKey.M) || _gamepad.WasPressed(GamepadButtons.View))
        {
            _requestSwitch(GameSceneId.WorldMap);
            return;
        }

        if (_input.ConsumePressed(VirtualKey.Tab) ||
            _input.ConsumeXButtonCyclePressed() ||
            _gamepad.WasPressed(GamepadButtons.B))
        {
            _player.CycleModel();
        }

        if (_input.ConsumePressed(VirtualKey.F7))
        {
            _worldLighting.CycleMode();
            _updateWindowTitle();
        }

        if (_worldLighting.Update(deltaSeconds, _scene.Lighting))
            _updateWindowTitle();

        _clickToMove.Update(
            _input,
            _camera,
            _viewportWidth(),
            _viewportHeight(),
            deltaSeconds);
        _camera.UpdateFromKeyboard(_input, deltaSeconds);
        _player.UpdatePose(_camera.WorldCenter, _camera.CameraSpeedUnitVector, deltaSeconds);
        _worldStreamer.Update(_camera.WorldCenter);
    }
}
