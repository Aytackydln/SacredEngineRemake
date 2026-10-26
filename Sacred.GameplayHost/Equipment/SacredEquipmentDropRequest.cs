using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Independent basic roll at the supplied hero/roll level. Enemy level,
/// source-specific drop chances and sets are separate modes. Null Quality rolls
/// the native base rarity distribution; 0..14 specifies its generation quality.
/// CategoryMode applies only when Category is null.</summary>
public readonly record struct SacredEquipmentDropRequest(ushort HeroLevel, uint Difficulty, uint Seed,
    SacredItemCategory? Category = null, uint HeroClass = 0, SacredDropCategoryMode CategoryMode = SacredDropCategoryMode.Uniform,
    uint? Quality = 0)
{
    public SacredLootRarityContext? RarityContext { get; init; }
}
