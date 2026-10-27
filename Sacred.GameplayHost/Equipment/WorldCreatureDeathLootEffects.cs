using Sacred.Core.Loot;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Original 0x559BF0 arguments after its entry gates. The downstream
/// invocation still owns chance budget, retries, selection and construction.</summary>
public readonly record struct WorldCreatureLootInvocation(uint ForcedResource, uint Bonus, float ChanceCurveInput, uint KillerReference);

/// <summary>Effects run in native order, sharing the caller's RNG. False is an
/// unresolved dependency, not a native empty result or permission to skip it.</summary>
public sealed record WorldCreatureDeathLootEffects(Func<bool> DetachEquipment,
    Func<SacredEquipmentRewardRequest, bool> EquipmentReward, Func<WorldCreatureLootInvocation, bool> Invocation,
    Func<uint, bool> WorldContainsResource, Func<SacredCreatureUniqueRewardQuery, bool> UniqueReward,
    Func<uint, uint> AuthoredResource);

public enum WorldCreatureDeathLootPending { None, EquipmentDetach, EquipmentReward, Invocation, UniqueReward }
public readonly record struct WorldCreatureDeathLootPrefixResult(SacredCreatureDeathLootEligibilityResult Eligibility,
    SacredCreatureDeathLootPlan? Plan, WorldCreatureDeathLootPending Pending)
{
    public bool Completed => Pending == WorldCreatureDeathLootPending.None;
}
