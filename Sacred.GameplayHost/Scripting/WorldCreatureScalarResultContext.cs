using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Resolved runtime queries for a nonlocal on-foot NPC or its empty
/// unassigned constructor calculation. Attachment, sets, arts/effects, health and events are
/// separate construction obligations; this result cannot grant readiness.</summary>
public sealed record WorldCreatureScalarResultContext(uint Resource, uint Reference,
    uint Difficulty, bool DifficultyBoostApplies, uint Regeneration, uint ActorFlags,
    uint ComponentFlags, byte CombatProfile, int LearnedCombatArtCount, int ActiveEffectCount,
    bool Mounted, SacredCreatureSkillContext Skills, uint LocalReference, SacredX87Precision Precision)
{
    /// <summary>The component calculates before the outer physical actor receives its type.</summary>
    public bool ActorTypeAssigned { get; init; } = true;
    /// <summary>Actual acquired-art sources for the pre-inventory local hero import.</summary>
    public SacredCreatureArtTimerInputs? SavedHeroArts { get; init; }
}

public sealed record WorldCreatureScalarEquipmentRecord(byte[] Values, SacredItemCategory Category, byte SharedSetType)
{
    /// <summary>Distinguishes a resolved missing modifier from an unsupplied query.</summary>
    public bool HasArmorModifierQuery { get; init; }
    public int? ArmorModifier { get; init; }
}
public sealed record WorldCreatureScalarEquipment(ushort Slot, IReadOnlyList<WorldCreatureScalarEquipmentRecord> Records);
public sealed record WorldCreatureScalarResult(byte[] Values, uint Regeneration, uint EquipmentFlagAdditions)
{
    public IReadOnlyList<byte[]>? CombatArts { get; init; }
}
