using System;

namespace Sacred.UI.Hud;

/// <summary>Renderer-independent capture and release-inside button behavior.</summary>
public sealed class BottomHudState
{
    public bool Visible { get; set; } = true;
    public BottomHudLayout? Layout { get; set; }
    /// <summary>Calendar fraction: midnight=0, noon=0.5. The host owns the clock.</summary>
    public float DayFraction { get; set; } = 0.5f;
    public HudButton? HoveredButton { get; set; }
    public HudButton? PressedButton { get; private set; }
    public bool PointerCaptured { get; private set; }
    public event Action<HudButton>? Activated;

    public void Press(HudButton? button)
    {
        PointerCaptured = true;
        PressedButton = button;
    }

    public void Release(HudButton? button)
    {
        var pressed = PressedButton;
        CancelPointer();
        if (pressed.HasValue && pressed == button) Activate(pressed.Value);
    }

    public void CancelPointer()
    {
        PointerCaptured = false;
        PressedButton = null;
        HoveredButton = null;
    }

    public void Activate(HudButton button) => Activated?.Invoke(button);
}
