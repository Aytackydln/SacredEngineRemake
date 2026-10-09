using System;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.UI.Hud;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Consumes HUD gestures before movement, using output coordinates independently of render scaling.</summary>
internal sealed class BottomHudInputController(BottomHudState hud, InputState input,
    Func<Vector2> viewport, Action<HudButton> activate)
{
    public void Activate(HudButton button)
    {
        EngineLog.WriteLine($"Debug input: bottom HUD {button} activated.");
        activate(button);
    }

    public void Update()
    {
        input.HudWantsMouse = false;
        if (!hud.Visible || hud.Layout is not { } layout || input.ImGuiWantsMouse)
        {
            hud.CancelPointer();
            return;
        }
        var size = viewport();
        hud.HoveredButton = layout.HitTest(input.MousePosition, size);
        if (input.PendingLeftClickPosition is { } press && layout.ContainsPointer(press, size))
        {
            input.TryConsumeLeftClick(out _);
            hud.Press(layout.HitTest(press, size));
            EngineLog.WriteLine($"Debug input: bottom HUD pressed {hud.PressedButton?.ToString() ?? "panel"}.");
        }
        var captured = hud.PointerCaptured;
        if (captured && input.ConsumeLeftMouseButtonReleased())
            hud.Release(hud.HoveredButton);
        else if (captured && !input.IsLeftMouseButtonDown)
            hud.CancelPointer();
        input.HudWantsMouse = captured || layout.ContainsPointer(input.MousePosition, size);
    }

    public void Cancel()
    {
        hud.CancelPointer();
        input.HudWantsMouse = false;
    }
}
