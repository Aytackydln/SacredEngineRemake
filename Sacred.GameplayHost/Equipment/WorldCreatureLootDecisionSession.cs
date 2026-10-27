using Sacred.Assets.Equipment;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

public readonly record struct WorldCreatureLootDecision(uint Call, SacredCreatureLootAttempt Attempt,
    SacredCreatureLootChoice Choice, uint RandomState);

/// <summary>Shared death-call/attempt/rarity/source-decision composition.
/// This stops before prepared resource selection, validation, special-resource
/// resolution and construction. Completing decisions does not complete a kill.</summary>
public sealed class WorldCreatureLootDecisionSession(SacredEquipmentGenerationData data)
{
    public SacredCreatureLootSelectionState SelectionState { get; } = new();

    public bool Run(WorldCreatureLootDecisionContext context, SacredRandomSequence random,
        Func<uint, bool> runeAvailable, Func<WorldCreatureLootDecision, SacredCreatureLootAttemptOutcome> resolve)
    {
        if (context.HeroLevel == 0 || context.Difficulty > 4)
            throw new ArgumentOutOfRangeException(nameof(context));
        var balance = data.Tuning.Loot ?? throw new InvalidOperationException("Original loot tuning unavailable.");
        var resources = data.CreatureLootResources ?? throw new InvalidOperationException("Original creature loot resources unavailable.");
        var categories = data.CategoryRules ?? throw new InvalidOperationException("Original loot categories unavailable.");
        var weights = data.Tuning.Magic?.RarityWeights ?? throw new InvalidOperationException("Original rarity weights unavailable.");
        var bonus = SacredCreatureDeathLootBonus.Calculate(context.SourceBonus, context.KillerReference,
            context.EffectiveFind, context.SurvivalSeconds, balance);
        var chance = SacredCreatureLootChance.Calculate(balance, context.NearbyPartyMembers, context.BoostedSource, context.ChanceCurveSkillValue);
        for (uint call = 0; call < context.SourceRollCount; call++)
        {
            if (!SacredCreatureLootAttempts.Run(chance, false, bonus, context.SourceReference,
                context.ExperienceMaximum, balance, random, attempt =>
                {
                    var rarity = SacredLootRarity.Creature(weights, context.SourceLevel, context.HeroLevel,
                        context.Difficulty, unchecked((ushort)attempt.Bonus), balance, random);
                    var choice = SacredCreatureLootSelection.Choose(new(context.SourceReference, context.SourceResource,
                        context.SourceCategory, context.SourceLevel, context.HeroResource, rarity.Selector, (byte)rarity.Quality),
                        SelectionState, resources, categories, balance, random,
                        () => SelectionState.RuneDropsDisabled ? 0 : SacredRuneLootChance.Calculate(context.SourceLevel, context.HeroLevel, context.HeroResource,
                            context.LearnedCombatArtRankSum, context.InventoryRuneCount, context.BoostedSource, balance, context.Difficulty), runeAvailable);
                    if (choice.Kind == SacredCreatureLootChoiceKind.Potion)
                        choice = choice with { Resource = SacredPotionLootSelection.Select(resources.PotionTable, context.Difficulty, false, -1, random) };
                    return resolve(new(call, attempt, choice, random.State));
                })) return false;
        }
        return true;
    }
}
