using System.Collections.Generic;
using System.Numerics;

namespace Sacred.Engine.Platform;

public sealed class InputState
{
    private readonly HashSet<VirtualKey> _down = [];
    private readonly HashSet<VirtualKey> _pressed = [];
    private Vector2? _leftClickPosition;
    private bool _leftMouseButtonReleased;
    
    public double LeftJoystickX { get; set; }
    
    public double LeftJoystickY { get; set; }

    public Vector2 MousePosition { get; private set; }

    public bool IsLeftMouseButtonDown { get; private set; }

    public bool IsDown(VirtualKey key) => _down.Contains(key);

    public bool ConsumePressed(VirtualKey key) => _pressed.Remove(key);

    public void SetMousePosition(int x, int y) => MousePosition = new Vector2(x, y);

    public void SetLeftMouseButton(bool down, int x, int y)
    {
        SetMousePosition(x, y);

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

    public void Set(VirtualKey key, bool down)
    {
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
}
