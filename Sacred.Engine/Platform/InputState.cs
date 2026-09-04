using System.Collections.Generic;
using System.Numerics;

namespace Sacred.Engine.Platform;

public sealed class InputState
{
    private readonly HashSet<VirtualKey> _down = [];
    private readonly HashSet<VirtualKey> _pressed = [];
    private Vector2? _leftClickPosition;
    private bool _leftMouseButtonReleased;
    private bool _rightMouseButtonPressed;
    private bool _rightMouseButtonReleased;
    private bool _middleMouseButtonDown;
    private bool _xButtonCyclePressed;
    private int _mouseWheelDelta;
    
    public double LeftJoystickX { get; set; }
    
    public double LeftJoystickY { get; set; }

    public double RightJoystickY { get; set; }

    public bool GamepadWalk { get; set; }

    public bool GamepadDefend { get; set; }

    public Vector2 MousePosition { get; private set; }

    public bool IsLeftMouseButtonDown { get; private set; }

    public bool IsRightMouseButtonDown { get; private set; }

    public bool IsMiddleMouseButtonDown => _middleMouseButtonDown;

    public int MouseWheelDelta => _mouseWheelDelta;

    public bool UiWantsMouse { get; private set; }

    public bool UiWantsKeyboard { get; private set; }

    public bool UsingController { get; private set; }

    public bool HasPendingLeftClick => _leftClickPosition.HasValue;

    public bool IsDown(VirtualKey key) => _down.Contains(key);

    public bool IsWalkModifierDown => IsDown(VirtualKey.Shift) || GamepadWalk;

    public bool IsDefendDown => IsDown(VirtualKey.Control) || GamepadDefend;

    public bool ConsumePressed(VirtualKey key) => _pressed.Remove(key);

    public void SetMousePosition(int x, int y)
    {
        MousePosition = new Vector2(x, y);
    }

    public void SetLeftMouseButton(bool down, int x, int y)
    {
        SetMousePosition(x, y);
        UsingController = false;

        if (down && !IsLeftMouseButtonDown)
            _leftClickPosition = MousePosition;
        else if (!down && IsLeftMouseButtonDown)
            _leftMouseButtonReleased = true;

        IsLeftMouseButtonDown = down;
    }

    public bool TryConsumeLeftClick(out Vector2 position)
    {
        if (_leftClickPosition is not { } clickPosition)
        {
            position = default;
            return false;
        }

        position = clickPosition;
        _leftClickPosition = null;
        return true;
    }

    public bool ConsumeLeftMouseButtonReleased()
    {
        if (!_leftMouseButtonReleased)
            return false;

        _leftMouseButtonReleased = false;
        return true;
    }

    public void SetRightMouseButton(bool down, int x, int y)
    {
        SetMousePosition(x, y);
        UsingController = false;
        if (down && !IsRightMouseButtonDown)
            _rightMouseButtonPressed = true;
        else if (!down && IsRightMouseButtonDown)
            _rightMouseButtonReleased = true;

        IsRightMouseButtonDown = down;
    }

    public bool ConsumeRightMouseButtonPressed()
    {
        if (!_rightMouseButtonPressed)
            return false;

        _rightMouseButtonPressed = false;
        return true;
    }

    public bool ConsumeRightMouseButtonReleased()
    {
        if (!_rightMouseButtonReleased)
            return false;

        _rightMouseButtonReleased = false;
        return true;
    }

    public void SetMiddleMouseButton(bool down, int x, int y)
    {
        SetMousePosition(x, y);
        UsingController = false;
        _middleMouseButtonDown = down;
    }

    public void DiscardPointerMovementEvents()
    {
        _leftClickPosition = null;
        _leftMouseButtonReleased = false;
    }

    public void PressXButtonCycle()
    {
        UsingController = false;
        _xButtonCyclePressed = true;
    }

    public bool ConsumeXButtonCyclePressed()
    {
        if (!_xButtonCyclePressed)
            return false;

        _xButtonCyclePressed = false;
        return true;
    }

    public void AddMouseWheelDelta(int delta) => _mouseWheelDelta += delta;

    public int ConsumeMouseWheelDelta()
    {
        var delta = _mouseWheelDelta;
        _mouseWheelDelta = 0;
        return delta;
    }

    public void Set(VirtualKey key, bool down)
    {
        UsingController = false;
        if (down)
        {
            if (_down.Add(key))
                _pressed.Add(key);
        }
        else
        {
            _down.Remove(key);
        }
    }

    public void MarkControllerInput() => UsingController = true;

    internal void SetUiCapture(bool mouse, bool keyboard)
    {
        UiWantsMouse = mouse;
        UiWantsKeyboard = keyboard;
    }

    public void DiscardUiCapturedPointerEvents()
    {
        DiscardPointerMovementEvents();
        _rightMouseButtonPressed = false;
        _rightMouseButtonReleased = false;
        _mouseWheelDelta = 0;
    }

    /// <summary>Discards one-shot events when input ownership moves to another scene.</summary>
    public void ClearTransientEvents()
    {
        _pressed.Clear();
        _leftClickPosition = null;
        _leftMouseButtonReleased = false;
        _rightMouseButtonPressed = false;
        _rightMouseButtonReleased = false;
        _xButtonCyclePressed = false;
        _mouseWheelDelta = 0;
        UiWantsMouse = false;
        UiWantsKeyboard = false;
    }
}
