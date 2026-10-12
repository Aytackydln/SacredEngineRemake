using System;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Console access to the same equipment operations used by the character controls.</summary>
internal static class PlayerEquipmentCheats
{
    public static bool TryExecute(string option, string value, PlayerCharacterController player, out string message)
    {
        message = string.Empty;
        switch (option.ToLowerInvariant())
        {
            case "equipment" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                foreach (var slot in player.CreateDebugPanelState().EquipmentSlots)
                    EngineLog.WriteLine($"Equipment slot {slot.SlotIndex}: {slot.SlotName}; {slot.EquipmentName ?? "empty"}; model {slot.EquipmentItemId?.ToString() ?? "none"}.");
                message = "player equipment listed"; return true;
            case "equipment" when value.Equals("clear", StringComparison.OrdinalIgnoreCase):
                player.RemoveAllEquipment(); message = "player equipment cleared"; return true;
            case "unequip" when int.TryParse(value, out var index):
                message = player.RemoveEquipment(index) ? $"equipment slot {index} cleared" : "slot empty or unavailable"; return true;
            case "equip" when uint.TryParse(value, out var modelId):
                message = player.EquipEquipment(modelId) ? $"equipment model {modelId} equipped" : "item unavailable or incompatible"; return true;
            case "equip-set" when int.TryParse(value, out var set):
                message = player.EquipItemSet(set) ? $"item set {set} equipped" : "set unavailable or incompatible"; return true;
            default: return false;
        }
    }
}
