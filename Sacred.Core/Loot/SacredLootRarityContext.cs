namespace Sacred.Core.Loot;

public enum SacredLootRarityProfile { Creature, Container }

/// <summary>Resolved inputs to a rarity stage, not a complete source event.
/// CreatureBonus is the resolved word after caller adjustment, whose upstream derivation is
/// separate from EquipmentFindBonus. UnlockMask uses SacredLootFind's four bits.</summary>
public readonly record struct SacredLootRarityContext(SacredLootRarityProfile Profile,
    float EquipmentFindBonus = 0, uint SurvivalSeconds = 0, ushort SourceLevel = 1,
    ushort CreatureBonus = 0, byte UnlockMask = 0)
{
    public ushort EffectiveFind(uint difficulty) => SacredLootFind.Calculate(EquipmentFindBonus, difficulty, UnlockMask);
}
