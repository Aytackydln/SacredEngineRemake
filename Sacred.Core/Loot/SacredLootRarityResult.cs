namespace Sacred.Core.Loot;

/// <summary>Keep the original selector: monster selectors zero/one dispatch to
/// special-resource selection, rather than ordinary equipment quality zero.</summary>
public readonly record struct SacredLootRarityResult(uint Selector, uint Quality)
{
    public bool RequiresSpecialResource => Selector is 0 or 1;
}
