using System;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.Inventory.Actors;
using Sacred.UI.CombatArts;
using Sacred.UI.Hud;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Consumes inventory gestures before click-to-move without capturing the rest of the world.</summary>
internal sealed class InventoryInputController(EquipmentUiState state, InputState input, Func<Vector2> viewport)
{
    private bool _captured;
    private Target? _pressed;
    private SacredGameActor? _actor;
    private InventoryTab _tab;
    public BottomHudState? Hud { get; init; }

    public void Update()
    {
        if (!ReferenceEquals(_actor, state.Actor) || _tab != state.ActiveTab)
        { _actor = state.Actor; _tab = state.ActiveTab; _captured = false; _pressed = null; state.ItemHover.Update(null, input.MousePosition); }
        if (state.Actor is null || input.ImGuiWantsMouse || Hud is not { Visible: true })
        { _captured = false; _pressed = null; state.ItemHover.Update(null, input.MousePosition); return; }
        var size = viewport();
        state.ItemHover.Update(HoverEquipment(input.MousePosition, size), input.MousePosition);
        if (input.PendingLeftClickPosition is { } point && Contains(point, size))
        {
            input.TryConsumeLeftClick(out _);
            _captured = true; _pressed = Hit(point, size);
        }
        var captured = _captured;
        if (_captured && input.ConsumeLeftMouseButtonReleased())
        {
            var released = Hit(input.MousePosition, size);
            if (_pressed?.Art is { } code && released?.QuickSlot is { } slot)
                Assign(code, slot);
            else if (_pressed is { } target && target == released)
            {
                if (target.Tab is { } tab) state.SetTab(tab);
                else if (target.Art is { } art)
                {
                    var selected = state.CombatArts.Select(art);
                    EngineLog.WriteLine($"Debug input: combat art {art}; {(selected ? "selected for a quickslot" : "unlearned")}.");
                }
                else if (target.QuickSlot is { } quickSlot)
                {
                    if (state.CombatArts.SelectedCode is { } selected) Assign(selected, quickSlot);
                    else Hud.SelectedCombatArtSlot = quickSlot;
                    EngineLog.WriteLine($"Debug input: combat quickslot {quickSlot} selected.");
                }
                else if (target.Equipment is { } index)
                {
                    state.Select(index);
                    var equipment = state.Actor!.EquipmentSlots[index];
                    EngineLog.WriteLine($"Debug input: inventory slot {index}; {equipment.Type}; {equipment.Equipment?.Name ?? "empty"} selected.");
                }
            }
            _captured = false; _pressed = null;
        }
        else if (!input.IsLeftMouseButtonDown) { _captured = false; _pressed = null; }
        input.HudWantsMouse |= captured || Contains(input.MousePosition, size);
    }

    private bool Contains(Vector2 point, Vector2 size) =>
        state.IsOpen && (InventoryPanelLayout.Bounds(size).Contains(point) || InventoryTabsLayout.Bounds(size).Contains(point)) ||
        QuickSlot(point, size).HasValue;

    private int? HoverEquipment(Vector2 point, Vector2 size)
    {
        if (state.IsOpen && state.Layout?.HitTest(point, size) is { } slot) return slot;
        if (Hud?.Layout is not { } layout || state.Actor is not { } actor ||
            (uint)Hud.SelectedWeaponSlot >= (uint)layout.QuickSlots.HandSlots.Count) return null;
        var bounds = BottomHudQuickSlotLayout.GetBounds(layout.QuickSlots.HandSlots[Hud.SelectedWeaponSlot], size);
        if (!bounds.Contains(point)) return null;
        int? left = null, right = null;
        for (var index = 0; index < actor.EquipmentSlots.Count; index++)
        {
            if (actor.EquipmentSlots[index].Instance is null) continue;
            if (actor.EquipmentSlots[index].Type == EquipmentSlotType.LeftHand) left = index;
            if (actor.EquipmentSlots[index].Type == EquipmentSlotType.RightHand) right = index;
        }
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return point.X < bounds.Position.X + bounds.Size.X / 2 ? left : right;
    }

    private int? QuickSlot(Vector2 point, Vector2 size)
    {
        if (Hud?.Layout is not { } layout) return null;
        for (var index = 0; index < layout.QuickSlots.CombatArtSlots.Count; index++)
            if (BottomHudQuickSlotLayout.GetBounds(layout.QuickSlots.CombatArtSlots[index], size).Contains(point)) return index;
        return null;
    }

    private Target? Hit(Vector2 point, Vector2 size)
    {
        if (QuickSlot(point, size) is { } slot) return new(QuickSlot: slot);
        if (!state.IsOpen) return null;
        if (state.Layout?.HitTest(point, size) is { } equipment) return new(Equipment: equipment);
        if (InventoryTabsLayout.HitTestTab(point, size) is { } tab) return new(Tab: tab);
        if (state.ActiveTab == InventoryTab.CombatArts && InventoryTabsLayout.Bounds(size).Contains(point))
        {
            var local = InventoryTabsLayout.ToLocal(point, size) - InventoryTabsLayout.ContentOrigin;
            return CombatArtPanelLayout.HitTest(local, state.CombatArts.Available.Count) is { } index
                ? new(Art: state.CombatArts.Available[index].Code) : null;
        }
        return null;
    }

    private void Assign(ushort code, int slot)
    {
        if (!state.CombatArts.Assign(code, slot, Hud?.Layout?.QuickSlots.CombatArtSlots.Count ?? 0)) return;
        Hud!.SelectedCombatArtSlot = slot;
        EngineLog.WriteLine($"Debug input: combat art {code} assigned to combat quickslot {slot}.");
    }

    private readonly record struct Target(InventoryTab? Tab = null, int? Equipment = null, ushort? Art = null, int? QuickSlot = null);
}
