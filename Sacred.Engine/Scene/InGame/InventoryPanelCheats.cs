using System;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Scene.InGame;

internal static class InventoryPanelCheats
{
    public static bool TryExecute(string option, string value, EquipmentUiState state, out string message)
    {
        message = string.Empty;
        if (!option.Equals("inventory", StringComparison.OrdinalIgnoreCase)) return false;
        switch (value.ToLowerInvariant())
        {
            case "open" or "on": state.SetOpen(true); break;
            case "close" or "off": state.SetOpen(false); break;
            case "toggle": state.SetOpen(!state.IsOpen); break;
            case "status": break;
            default:
                if (!int.TryParse(value, out var index) || state.Actor is not { } actor ||
                    (uint)index >= (uint)actor.EquipmentSlots.Count)
                { message = "inventory: open, close, toggle, status or slot index"; return true; }
                state.Select(index); break;
        }
        message = $"inventory {(state.IsOpen ? "open" : "closed")}; {state.Actor?.CharacterClass}; " +
            $"{state.Layout?.Slots.Count ?? 0} armor/accessory controls; stats artwork unbound; " +
            $"selected {state.SelectedSlot?.ToString() ?? "none"}";
        return true;
    }
}
