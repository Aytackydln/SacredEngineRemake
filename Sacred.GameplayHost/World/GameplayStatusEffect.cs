using Sacred.Particles;

namespace Sacred.GameplayHost.World;

/// <summary>One locally executed CA visual retaining source identity and wearer lifetime.</summary>
public sealed record GameplayStatusEffect(long Generation, SacredCombatVisualDefinition Definition,
    uint Caster, long CasterInstance, uint Owner, long OwnerInstance, int Channel, int Rank,
    float Duration, string? PendingReason)
{
    public float Age { get; internal set; }
    public bool VisualAvailable { get; init; }
    public uint Type => Definition.Resource;
    public int Preset => 0;
    public string Bone => Definition.Bone;
}
