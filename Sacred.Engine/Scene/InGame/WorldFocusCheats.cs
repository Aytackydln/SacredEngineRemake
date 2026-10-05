using System;
using System.Globalization;
using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

internal static class WorldFocusCheats
{
    public static bool TrySetOption(string option, string value, InGameInputController input,
        DoorSceneController objects, SacredCamera camera, byte level, out string message)
    {
        message = string.Empty;
        if (!option.Equals("focus", StringComparison.OrdinalIgnoreCase)) return false;
        var focus = input.Focus;
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase)) message = focus.Status;
        else if (value.Equals("clear", StringComparison.OrdinalIgnoreCase))
        { input.CancelInteraction(); focus.Clear(); camera.DebugManualDirection = null; camera.StopMoving(); message = "focus probes cleared"; }
        else if (value.Equals("stop", StringComparison.OrdinalIgnoreCase))
        { input.CancelInteraction(); camera.DebugManualDirection = null; camera.StopMoving(); message = "console steering stopped"; }
        else if (value.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in objects.FocusTargets)
                if (Vector2.DistanceSquared(candidate.Position, camera.WorldCenter) <= 100)
                    EngineLog.WriteLine($"Focus candidate: {candidate.Id} at {candidate.Position.X:0.###},{candidate.Position.Y:0.###}; {candidate.Model!.Name}");
            message = focus.Status;
        }
        else if (value.Equals("interact", StringComparison.OrdinalIgnoreCase))
            message = $"focus interaction {(input.RequestInteraction() ? "accepted" : "unavailable")}; {focus.Status}";
        else if (value.Equals("reach", StringComparison.OrdinalIgnoreCase))
            message = focus.Target is { } target
                ? $"interaction distance {Vector2.Distance(camera.WorldCenter, WorldInteractionApproach.ClosestPoint(target, camera.WorldCenter)):0.###} tiles; in reach {WorldInteractionApproach.InReach(target, camera.WorldCenter)}; {focus.Status}"
                : "focus none";
        else if (value.Equals("assist", StringComparison.OrdinalIgnoreCase))
            message = $"stairs assist: {focus.AssistStairs(camera.WorldCenter, focus.DebugDirection ?? camera.CharacterFacingUnitVector)?.ToString() ?? "none"}; {focus.Status}";
        else if (value.Split(':', 2) is [var mode, var vector] && TryVector(vector, out var position))
        {
            if (mode.Equals("cone", StringComparison.OrdinalIgnoreCase) && position.LengthSquared() > 0.000001f)
            { focus.DebugDirection = Vector2.Normalize(position); focus.DebugMouseWorld = null; focus.DebugMouseScreen = null; }
            else if (mode.Equals("walk", StringComparison.OrdinalIgnoreCase) && position.LengthSquared() > 0.000001f)
            { camera.DebugManualDirection = Vector2.Normalize(position); focus.DebugDirection = camera.DebugManualDirection;
                focus.DebugMouseWorld = null; focus.DebugMouseScreen = null; }
            else if (mode.Equals("mouse", StringComparison.OrdinalIgnoreCase))
            { focus.DebugMouseWorld = position; focus.DebugDirection = null; focus.DebugMouseScreen = null; }
            else if (mode.Equals("pointer", StringComparison.OrdinalIgnoreCase))
            { focus.DebugMouseScreen = position; focus.DebugDirection = null; focus.DebugMouseWorld = null; }
            else return false;
            message = $"focus {mode} probe at {position}; no platform input generated";
        }
        else message = "focus: status, list, clear, interact, reach, assist, stop, walk:x,y, cone:x,y (world direction), mouse:x,y (world point), pointer:x,y (render pixels)";
        return true;
    }

    private static bool TryVector(string value, out Vector2 result)
    {
        result = default;
        if (value.Split(',') is not [var x, var y] ||
            !float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ||
            !float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var py) ||
            !float.IsFinite(px) || !float.IsFinite(py)) return false;
        result = new Vector2(px, py); return true;
    }
}
