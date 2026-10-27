namespace Sacred.Core.Loot;

/// <summary>Original instruction operands/tables for creature source selection.</summary>
public sealed record SacredCreatureLootResources(IReadOnlyList<uint> MoneyResources,
    IReadOnlyList<uint> RuneResources, IReadOnlyList<uint> RuneExcludedSources,
    uint FallbackResource)
{
    public IReadOnlyList<uint> PotionTable { get; init; } = [];
    public IReadOnlyList<uint> ProgressionForcedResources { get; init; } = [];
    public int MaximumAcceptedResource { get; init; }
}
