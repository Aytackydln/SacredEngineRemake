using Sacred.Core.Pak.Items;
using Sacred.Core.World.Portals;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

public sealed class EffectAssetRow(ItemsPakEntry item, SacredParticleDefinition? definition)
{
    internal ItemsPakEntry Item { get; } = item;
    internal SacredParticleDefinition? Definition { get; } = definition;
    internal SacredPortalVariant? Portal => SacredPortalAppearance.TryGetVariant(Item.ModelName, out var variant) ? variant : null;
    public ushort EntryId => Item.ItemIndex;
    public string Effect => Item.ModelName;
    public uint? NativeType => Definition?.TypeId;
    public string NativeName => Definition?.TypeName ?? string.Empty;
    public uint ResourceId => Item.ModelDesc.ResourceId;
    public string Family { get; internal init; } = string.Empty;
    public string FamilyCoverage { get; internal init; } = string.Empty;
    public bool Implemented => Portal is not null || Definition?.Status == SacredParticleDefinitionStatus.Decoded || EventVariants > 0;
    public string Status => Portal is not null ? "Decoded mesh" : Definition?.Status == SacredParticleDefinitionStatus.Decoded ? "Decoded"
        : EventVariants > 0 ? "Event variants" : Definition?.Status.ToString() ?? "Unmapped";
    public int EventVariants { get; internal init; }
}
