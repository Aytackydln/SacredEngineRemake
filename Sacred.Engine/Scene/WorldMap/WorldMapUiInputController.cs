using System;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.UI.Maps;

namespace Sacred.Engine.Scene.WorldMap;

/// <summary>Consumes map UI gestures before map targeting, dragging and wheel input.</summary>
internal sealed class WorldMapUiInputController(WorldMapUiState state, InputState input, Func<Vector2> viewport)
{
    public void Update()
    {
        input.WorldMapUiWantsMouse = false;
        if (!state.Visible || state.Layout is not { } layout || input.ImGuiWantsMouse)
        { state.CancelPointer(); return; }
        var size = viewport();
        var hovered = layout.HitTest(input.MousePosition, size);
        if (input.PendingLeftClickPosition is { } press && layout.ContainsPointer(press, size))
        {
            input.TryConsumeLeftClick(out _);
            state.Press(layout.HitTest(press, size));
            EngineLog.WriteLine($"Debug input: map UI pressed {state.PressedButton?.ToString() ?? "inactive control or frame"}.");
        }
        var captured = state.PointerCaptured;
        if (captured && input.ConsumeLeftMouseButtonReleased())
        {
            EngineLog.WriteLine($"Debug input: map UI released over {hovered?.ToString() ?? "outside"}.");
            state.Release(hovered);
        }
        else if (captured && !input.IsLeftMouseButtonDown) state.CancelPointer();
        state.HoveredButton = hovered;
        input.WorldMapUiWantsMouse = captured || layout.ContainsPointer(input.MousePosition, size);
    }

    public void Reset()
    {
        state.CancelPointer();
        input.WorldMapUiWantsMouse = false;
    }

    public bool TryCheat(string value, out string message)
    {
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = $"map UI {(state.Layout is null ? "loading" : "ready")}; close available; waypoint inactive";
            return true;
        }
        if (Enum.TryParse<WorldMapUiButton>(value, true, out var button) && Enum.IsDefined(button))
        {
            message = state.Activate(button) ? $"map UI {button} requested" : $"map UI {button} unavailable";
            return true;
        }
        message = "map-ui: close, waypoint or status";
        return true;
    }
}
