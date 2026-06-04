using System.Collections.Generic;

namespace Sacred.Engine.Platform;

public sealed class InputState
{
    private readonly HashSet<VirtualKey> _down = [];
    private readonly HashSet<VirtualKey> _pressed = [];
    
    public double LeftJoystickX { get; set; }
    
    public double LeftJoystickY { get; set; }

    public bool IsDown(VirtualKey key) => _down.Contains(key);

    public bool ConsumePressed(VirtualKey key) => _pressed.Remove(key);

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
