using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Original source canonicalization followed by the shared Rust.bin
/// lookup. This includes authored NPC resources, not only the hero selectors.</summary>
public sealed class WorldEquipmentVariants(SacredEquipmentGenerationData data)
{
    public SacredEquipmentVariantResult Resolve(uint itemResource, uint actorResource)
    {
        var sources = data.EquipmentSources ?? throw new InvalidOperationException("Original equipment source aliases unavailable.");
        return SacredEquipmentVariantLookup.ResolveCanonicalSource(itemResource, sources.CanonicalResource(actorResource),
            data.ClassGate.MinimumReplacementSource, data.Classes, data.Variants, data.ParentResource,
            type => data.Templates.Items.TryGetValue(type, out var item) ? item.ModelDesc : null);
    }
}
