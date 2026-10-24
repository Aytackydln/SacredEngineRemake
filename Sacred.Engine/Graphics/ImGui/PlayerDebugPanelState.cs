using System.Collections.Generic;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Frame-safe player data displayed by the immediate-mode debug UI.</summary>
internal sealed record PlayerDebugPanelState(
    string CharacterName,
    uint SelectedCharacterEntryId,
    IReadOnlyList<PlayerCharacterPresetState> CharacterPresets,
    IReadOnlyList<PlayerEquipmentSlotState> EquipmentSlots,
    IReadOnlyList<PlayerItemSetState> ItemSets)
{
    public ushort Level { get; init; } = 1;
    public int LearnedCombatArts { get; init; }
    public int AvailableCombatArts { get; init; }
    public string CombatStatus { get; init; } = string.Empty;
}

internal readonly record struct PlayerCharacterPresetState(uint EntryId, string DisplayName);

internal readonly record struct PlayerEquipmentSlotState(
    int SlotIndex,
    string SlotName,
    string? EquipmentName,
    uint? EquipmentItemId);

internal readonly record struct PlayerItemSetState(
    int SetIndex,
    uint SetIdentifier,
    string SetName,
    int ItemCount,
    int ResolvedEquipmentCount);
