using System;
using Windows.Gaming.Input;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Separates tap-to-map and hold-to-minimap input semantics.</summary>
internal sealed class InGameMapInputController
{
    private const float SelectHoldThresholdSeconds = 0.25f;

    private readonly InputState _input;
    private readonly GamepadInputSource _gamepad;
    private readonly MinimapOverlayState _minimap;
    private readonly Action<GameSceneId> _requestSwitch;

    private float _selectHeldSeconds;
    private bool _selectWasDown;
    private bool _selectOpenedMinimap;
    private bool _ignoreSelectUntilReleased;

    public InGameMapInputController(
        InputState input,
        GamepadInputSource gamepad,
        MinimapOverlayState minimap,
        Action<GameSceneId> requestSwitch)
    {
        _input = input;
        _gamepad = gamepad;
        _minimap = minimap;
        _requestSwitch = requestSwitch;
    }

    /// <returns><see langword="true"/> when a scene switch was requested.</returns>
    public bool Update(float deltaSeconds)
    {
        // TAB is state-driven, but discard its edge so it cannot become a stale press.
        _input.ConsumePressed(VirtualKey.Tab);

        if (_input.ConsumePressed(VirtualKey.M))
        {
            SetMinimapVisible(false);
            EngineLog.WriteLine("World map requested by keyboard.");
            _requestSwitch(GameSceneId.WorldMap);
            return true;
        }

        var selectDown = _gamepad.IsDown(GamepadButtons.View);
        if (_ignoreSelectUntilReleased)
        {
            if (!selectDown)
                _ignoreSelectUntilReleased = false;

            _selectWasDown = selectDown;
            SetMinimapVisible(_input.IsDown(VirtualKey.Tab) || _input.IsMiddleMouseButtonDown);
            return false;
        }

        if (selectDown)
        {
            if (!_selectWasDown)
            {
                _selectHeldSeconds = 0.0f;
                _selectOpenedMinimap = false;
            }

            _selectHeldSeconds += Math.Max(0.0f, deltaSeconds);
            if (_selectHeldSeconds >= SelectHoldThresholdSeconds)
                _selectOpenedMinimap = true;
        }
        else if (_selectWasDown)
        {
            if (!_selectOpenedMinimap)
            {
                ResetSelectGesture();
                SetMinimapVisible(false);
            EngineLog.WriteLine("World map requested by controller SELECT tap.");
                _requestSwitch(GameSceneId.WorldMap);
                return true;
            }

            ResetSelectGesture();
        }

        _selectWasDown = selectDown;
        SetMinimapVisible(
            _input.IsDown(VirtualKey.Tab) ||
            _input.IsMiddleMouseButtonDown ||
            (selectDown && _selectOpenedMinimap));
        return false;
    }

    public void OnActivated()
    {
        ResetSelectGesture();
        _ignoreSelectUntilReleased = _gamepad.IsDown(GamepadButtons.View);
        _selectWasDown = _ignoreSelectUntilReleased;
        SetMinimapVisible(false);
    }

    public void OnDeactivated()
    {
        ResetSelectGesture();
        _ignoreSelectUntilReleased = false;
        SetMinimapVisible(false);
    }

    private void ResetSelectGesture()
    {
        _selectHeldSeconds = 0.0f;
        _selectWasDown = false;
        _selectOpenedMinimap = false;
    }

    private void SetMinimapVisible(bool visible)
    {
        if (_minimap.IsVisible == visible)
            return;

        _minimap.IsVisible = visible;
        EngineLog.WriteLine(visible ? "Minimap opened." : "Minimap closed.");
    }
}
