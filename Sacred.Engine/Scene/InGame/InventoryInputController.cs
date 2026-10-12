using System;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.Inventory.Actors;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Consumes inventory gestures before click-to-move without capturing the rest of the world.</summary>
internal sealed class InventoryInputController(EquipmentUiState state, InputState input, Func<Vector2> viewport)
{
    private bool _captured;
    private int? _pressed;
    private SacredGameActor? _actor;

    public void Update()
    {
        if (!ReferenceEquals(_actor, state.Actor))
        { _actor = state.Actor; _captured = false; _pressed = null; }
        if (!state.IsOpen || input.ImGuiWantsMouse)
        { _captured = false; _pressed = null; return; }
        var size = viewport();
        if (input.PendingLeftClickPosition is { } point && InventoryPanelLayout.Bounds(size).Contains(point))
        {
            input.TryConsumeLeftClick(out _);
            _captured = true; _pressed = state.Layout?.HitTest(point, size);
        }
        var captured = _captured;
        if (_captured && input.ConsumeLeftMouseButtonReleased())
        {
            if (_pressed is { } index && state.Layout?.HitTest(input.MousePosition, size) == index)
            {
                state.Select(index);
                var slot = state.Actor!.EquipmentSlots[index];
                EngineLog.WriteLine($"Debug input: inventory slot {index}; {slot.Type}; {slot.Equipment?.Name ?? "empty"} selected.");
            }
            _captured = false; _pressed = null;
        }
        else if (!input.IsLeftMouseButtonDown) { _captured = false; _pressed = null; }
        input.HudWantsMouse |= captured || InventoryPanelLayout.Bounds(size).Contains(input.MousePosition);
    }
}
