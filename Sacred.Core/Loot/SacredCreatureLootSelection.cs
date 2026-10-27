using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x55A391..0x55A7FC: sequential source choices. This uses the
/// general loot arrays, including horse, rather than equipment-viewer filters.</summary>
public static class SacredCreatureLootSelection
{
    public static SacredCreatureLootChoice Choose(SacredCreatureLootSource source,
        SacredCreatureLootSelectionState state, SacredCreatureLootResources resources,
        SacredItemCategoryRules categories, SacredLootBalance balance, SacredRandomSequence random,
        Func<double> runeThreshold, Func<uint, bool> runeAvailable)
    {
        if (random.Next() % 1000 < balance.CreaturePotionChance) return Potion();
        if (source.Category != 6 && state.LastRuneSource != source.Reference)
        {
            var sample = (float)(random.Next() % 1000 * 30f);
            if (runeThreshold() > sample)
            {
                state.LastRuneSource = source.Reference;
                var rune = resources.RuneResources[random.Next() % resources.RuneResources.Count];
                if (resources.RuneExcludedSources.Contains(source.Resource) || !runeAvailable(rune)) return Potion();
                return new(SacredCreatureLootChoiceKind.Resource, Math.Max(source.Selector, 16), rune, EmitRuneEffect: true);
            }
        }
        // Preserve the short circuit order: the second money sample is drawn
        // even for sources outside category 11 when the first checks fail.
        if (random.Next() % 1000 < balance.CreatureMoneyChance || source.Category == 6 ||
            (random.Next() % 1000 < 600 && source.Category == 11))
        {
            var tier = 0;
            if (random.Next() % 13 == 0 && source.Level >= 5)
            {
                tier = 1;
                if (random.Next() % 7 == 0 && source.Level >= 10)
                {
                    tier = 2;
                    if (random.Next() % 4 == 0 && source.Level >= 20) tier = 3;
                }
            }
            return new(SacredCreatureLootChoiceKind.Resource, Math.Max(source.Selector, 16), resources.MoneyResources[tier]);
        }
        if (random.Next() % 1000 < balance.CreatureWeaponChance && source.Category != 11)
        {
            var preference = (byte)(random.Next() % 3 != 0 ? 2 : 0);
            return Equipment(SacredItemCategory.Weapon, preference);
        }
        if (random.Next() % 1000 < balance.CreatureArmorChance && source.Category != 11)
        {
            var preference = (byte)(random.Next() % 3 != 0 ? 2 : 0);
            return Equipment(SacredItemCategorySelection.Slots(categories.CreatureArmorCategories, random, categories.ArmorRetryCategory), preference);
        }
        if (state.LastJewelrySource == source.Reference)
            return new(SacredCreatureLootChoiceKind.Resource, source.Selector, resources.FallbackResource);
        state.LastJewelrySource = source.Reference;
        return Equipment(SacredItemCategorySelection.Slots(categories.CreatureJewelryCategories, random), 0);

        SacredCreatureLootChoice Potion() => new(SacredCreatureLootChoiceKind.Potion, Math.Max(source.Selector, 16));
        SacredCreatureLootChoice Equipment(SacredItemCategory category, byte preference) => new(SacredCreatureLootChoiceKind.Equipment,
            source.Selector, Category: category, HeroPreference: random.Next() % 2 == 0 ? source.HeroResource : 0,
            PreferenceFlags: preference, Quality: source.Quality);
    }
}
