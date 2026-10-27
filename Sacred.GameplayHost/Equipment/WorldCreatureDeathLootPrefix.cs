using Sacred.Assets.Equipment;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Shared Gold death gate, source plan/rewards, caller bonus and loot
/// entry composition. Post-entry invocation bodies remain explicit dependencies;
/// completing this prefix does not complete a kill or produce concrete items.</summary>
public sealed class WorldCreatureDeathLootPrefix(SacredEquipmentGenerationData data)
{
    public WorldCreatureDeathLootPrefixResult Run(WorldCreatureDeathLootContext context,
        SacredRandomSequence random, WorldCreatureDeathLootEffects effects)
    {
        var balance = data.Tuning.Loot ?? throw new InvalidOperationException("Original loot tuning unavailable.");
        var gateRules = data.CreatureDeathLootRules ?? throw new InvalidOperationException("Original death gate unavailable.");
        var rewardRules = data.CreatureDeathRewardRules ?? throw new InvalidOperationException("Original death reward rules unavailable.");
        var resources = data.CreatureLootResources ?? throw new InvalidOperationException("Original loot entry resources unavailable.");
        var source = context.Source;
        var hero = context.Hero;
        var pending = WorldCreatureDeathLootPending.None;
        var gate = SacredCreatureDeathLootEligibility.Evaluate(source.Actor, source.PhysicalFlags, context.Killer, gateRules,
            balance, random, () => hero.Actor,
            () => hero.Actor.HasValue && hero.Observer is { } observer && observer.CanReceiveLoot(context.HostileToObserver),
            reference => hero.CompanionReferences.Contains(reference),
            () => Effect(effects.DetachEquipment(), WorldCreatureDeathLootPending.EquipmentDetach));
        if (gate.Gate != SacredCreatureDeathLootGate.Ready) return new(gate, null, pending);
        var killer = gate.Killer!.Value;
        var plan = SacredCreatureDeathLootPlan.Create(source.Level, hero.Level, context.Difficulty,
            (source.PhysicalFlags & 0x400) != 0, (source.Actor.Behavior & 0x80000) != 0, balance, random);
        var dispatch = SacredCreatureDeathRewardDispatcher.Run(plan, source.Actor.Resource, source.Level, killer, data.Sets,
            rewardRules, balance, random, new(
                request => Effect(effects.EquipmentReward(request), WorldCreatureDeathLootPending.EquipmentReward),
                resource => Invoke(resource, 0, 0, 0), effects.WorldContainsResource,
                query => Effect(effects.UniqueReward(query), WorldCreatureDeathLootPending.UniqueReward)));
        if (!dispatch.Completed) return new(gate, dispatch.Plan, pending);
        plan = dispatch.Plan;
        var bonus = SacredCreatureDeathLootBonus.Calculate(plan.Bonus, killer.Reference, hero.EffectiveFind, hero.SurvivalSeconds, balance);
        for (uint call = 0; call < plan.Count; call++)
            if (!Invoke(0, bonus, hero.ChanceCurveSkillValue, killer.Reference)) break;
        return new(gate, plan, pending);

        bool Invoke(uint forcedResource, uint bonus, float curveInput, uint killerReference)
        {
            var entry = SacredCreatureLootEntry.Resolve(forcedResource, source.Actor.Resource, source.Destroyed,
                source.Actor.ResourceClass, hero.Actor.HasValue ? hero.Progression22 : (byte)0,
                resources.ProgressionForcedResources, random, effects.AuthoredResource);
            return entry.Kind != SacredCreatureLootEntryKind.Proceed ||
                Effect(effects.Invocation(new(entry.ForcedResource, bonus, curveInput, killerReference)), WorldCreatureDeathLootPending.Invocation);
        }
        bool Effect(bool complete, WorldCreatureDeathLootPending stage)
        {
            if (!complete) pending = stage;
            return complete;
        }
    }
}
