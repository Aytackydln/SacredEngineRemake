namespace Sacred.GameplayHost.Equipment;

/// <summary>Category-stage sampling. Caller presets do not simulate the full
/// source's drop chance, quality, world state or preceding random draws.</summary>
public enum SacredDropCategoryMode
{
    Uniform,
    NativeWeighted,
    Reward,
    CreatureArmor,
    CreatureJewelry
}
