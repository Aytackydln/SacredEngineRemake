using System;

namespace Sacred.UI.Maps;

public sealed class WorldMapUiState
{
    public bool Visible { get; set; }
    public WorldMapUiLayout? Layout { get; set; }
    public WorldMapUiButton? HoveredButton { get; set; }
    public WorldMapUiButton? PressedButton { get; private set; }
    public bool PointerCaptured { get; private set; }
    public event Action? CloseRequested;

    public void Press(WorldMapUiButton? button)
    {
        CancelPointer();
        if (!Visible) return;
        PointerCaptured = true;
        if (button == WorldMapUiButton.Close) PressedButton = button;
    }

    public void Release(WorldMapUiButton? button)
    {
        var pressed = PressedButton;
        CancelPointer();
        if (pressed.HasValue && pressed == button) Activate(pressed.Value);
    }

    public bool Activate(WorldMapUiButton button)
    {
        if (!Visible || button != WorldMapUiButton.Close) return false;
        CancelPointer();
        CloseRequested?.Invoke();
        return true;
    }

    public void CancelPointer()
    {
        HoveredButton = null;
        PressedButton = null;
        PointerCaptured = false;
    }
}
