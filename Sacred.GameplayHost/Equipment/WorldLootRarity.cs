using Sacred.Assets.Equipment;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Shared original rarity stages for source callers and the viewer laboratory.</summary>
public sealed class WorldLootRarity(SacredEquipmentGenerationData data)
{
    public SacredLootRarityResult Roll(SacredLootRarityContext context, ushort heroLevel, uint difficulty, SacredRandomSequence random)
    {
        if (!Enum.IsDefined(context.Profile)) throw new ArgumentOutOfRangeException(nameof(context));
        if (!float.IsFinite(context.EquipmentFindBonus) || context.UnlockMask > 15)
            throw new ArgumentOutOfRangeException(nameof(context));
        var balance = data.Tuning.Loot ?? throw new InvalidOperationException("Original loot tuning unavailable.");
        var weights = data.Tuning.Magic?.RarityWeights ?? throw new InvalidOperationException("Original rarity weights unavailable.");
        return context.Profile switch
        {
            SacredLootRarityProfile.Creature => SacredLootRarity.Creature(weights, context.SourceLevel, heroLevel,
                difficulty, context.CreatureBonus, balance, random),
            SacredLootRarityProfile.Container => SacredLootRarity.Container(weights, context.EffectiveFind(difficulty),
                context.SurvivalSeconds, balance, random),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }
}
