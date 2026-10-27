using System;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Scene.InGame;

internal static class InventoryPanelCheats
{
    public static bool TryExecute(string option, string value, EquipmentUiState state, out string message)
    {
        message = string.Empty;
        if (option.Equals("inventory-hover", StringComparison.OrdinalIgnoreCase))
        {
            if (value is "off" or "auto" or "clear") state.ItemHover.Override(null);
            else if (int.TryParse(value, out var slot) && state.Actor is { } actor && (uint)slot < (uint)actor.EquipmentSlots.Count)
            {
                state.SetOpen(true);
                state.ItemHover.Override(slot);
            }
            else { message = "inventory-hover: equipment slot index, off or auto"; return true; }
            message = $"Item hover: {state.ItemHover.Slot?.ToString() ?? "none"}.";
            return true;
        }
        if (!option.Equals("inventory", StringComparison.OrdinalIgnoreCase)) return false;
        switch (value.ToLowerInvariant())
        {
            case "open" or "on": state.SetOpen(true); break;
            case "close" or "off": state.SetOpen(false); break;
            case "toggle": state.SetOpen(!state.IsOpen); break;
            case "status": break;
            case "inventory": state.SetTab(InventoryTab.Inventory); state.SetOpen(true); break;
            case "combatarts" or "combat-arts": state.SetTab(InventoryTab.CombatArts); state.SetOpen(true); break;
            default:
                if (!int.TryParse(value, out var index) || state.Actor is not { } actor ||
                    (uint)index >= (uint)actor.EquipmentSlots.Count)
                { message = "inventory: open, close, toggle, status, inventory, combatarts or equipment slot index"; return true; }
                state.Select(index); break;
        }
        message = $"inventory {(state.IsOpen ? "open" : "closed")}; {state.Actor?.CharacterClass}; " +
            $"tab {state.ActiveTab}; combat arts {state.CombatArts.Available.Count}; " +
            $"{state.Layout?.Slots.Count ?? 0} armor/accessory controls; stats artwork unbound; " +
            $"selected {state.SelectedSlot?.ToString() ?? "none"}";
        return true;
    }
}
