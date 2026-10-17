using System;
using System.Globalization;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private bool TrySetServerMoveOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server-move", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if (value.Equals("status", StringComparison.OrdinalIgnoreCase)) { message = RuntimeScene.NetworkMovementStatus; return true; }
            if (value.Equals("stop", StringComparison.OrdinalIgnoreCase)) { RuntimeScene.StopNetworkMovement(); message = "network navigation stopped"; return true; }
            var coordinates = value.Split(',');
            if (coordinates.Length != 2 || !float.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !float.IsFinite(x) || !float.IsFinite(y))
                throw new ArgumentException("Use set server-move <tile-x,tile-y>, status or stop.");
            if (_serverWalk is { IsComplete: false }) throw new InvalidOperationException("Stop the explicit patrol before using normal navigation.");
            RuntimeScene.NavigateNetworkTo(new(x, y));
            message = $"network navigation requested: {x:0.###}, {y:0.###}";
        }
        catch (Exception error) { message = "network navigation failed: " + error.Message; }
        return true;
    }
}
