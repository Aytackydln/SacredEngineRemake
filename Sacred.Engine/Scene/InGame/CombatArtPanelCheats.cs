using System;
using System.Linq;
using Sacred.UI.Hud;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Uses the same selection and assignment rules as pointer input, without desktop automation.</summary>
internal static class CombatArtPanelCheats
{
    public static bool TryExecute(string option, string value, EquipmentUiState state, BottomHudState hud, out string message)
    {
        message = string.Empty;
        switch (option.ToLowerInvariant())
        {
            case "combat-art" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                foreach (var art in state.CombatArts.Available)
                    EngineLog.WriteLine($"Combat art {art.Code}: rank {state.CombatArts.Rank(art.Code)}; selectable {state.CombatArts.CanAssign(art.Code)}.");
                message = $"{state.Actor?.CharacterClass}: {state.CombatArts.Available.Count} combat arts; selected {state.CombatArts.SelectedCode?.ToString() ?? "none"}; " +
                    $"quickslots {string.Join(", ", state.CombatArts.QuickSlots.Select((code, index) => $"{index}:{code?.ToString() ?? "empty"}"))}";
                return true;
            case "combat-art":
                message = ushort.TryParse(value, out var code) && state.CombatArts.Select(code)
                    ? $"combat art {code} selected; click a combat quickslot to assign"
                    : "combat-art: status or a learned combat art code from this character's pool";
                return true;
            case "combat-slot":
                if (int.TryParse(value, out var slot) && state.CombatArts.SelectedCode is { } selected &&
                    state.CombatArts.Assign(selected, slot, hud.Layout?.QuickSlots.CombatArtSlots.Count ?? 0))
                {
                    hud.SelectedCombatArtSlot = slot;
                    message = $"combat art {selected} assigned to combat quickslot {slot}";
                }
                else message = "Select a learned combat art first, then use combat-slot with a visible zero-based slot index.";
                return true;
            default: return false;
        }
    }
}
