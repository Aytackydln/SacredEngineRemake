using System;
using System.Globalization;
using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

internal static class PlayerMovementCheats
{
    public static bool TrySetOption(string option, string value, SacredCamera camera,
        InGameInputController input, out string message)
    {
        message = string.Empty;
        switch (option.ToLowerInvariant())
        {
            case "path" when value.Split(',') is [var pathX, var pathY] &&
                float.TryParse(pathX, NumberStyles.Float, CultureInfo.InvariantCulture, out var targetX) &&
                float.TryParse(pathY, NumberStyles.Float, CultureInfo.InvariantCulture, out var targetY) &&
                float.IsFinite(targetX) && float.IsFinite(targetY):
                input.NavigateTo(new Vector2(targetX, targetY));
                message = $"pathfinding toward {targetX},{targetY}";
                return true;
            case "move" when value.Equals("stop", StringComparison.OrdinalIgnoreCase):
                input.CancelInteraction(); camera.StopMoving(); message = "movement stopped"; return true;
            case "move" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                message = $"player {camera.WorldCenter.X:F3},{camera.WorldCenter.Y:F3}; " +
                    $"movement {camera.CurrentMovementSpeed:F3} units/s; animation {camera.LocomotionAnimationSpeed:F3}x";
                return true;
            case "movement-speed" when float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var multiplier) && float.IsFinite(multiplier):
                input.SetPlayerMovementSpeedMultiplier(multiplier);
                message = $"movement speed {input.PlayerMovementSpeedMultiplier:F2}x";
                return true;
            case "move" when value.Split(',') is [var moveX, var moveY] &&
                float.TryParse(moveX, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(moveY, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                float.IsFinite(x) && float.IsFinite(y):
                camera.MoveTo(new Vector2(x, y));
                message = $"moving toward {x},{y}";
                return true;
            default:
                return false;
        }
    }
}
