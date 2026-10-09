using Sacred.Particles.Generated;

namespace Sacred.Particles;

/// <summary>Equipment attachment rules extracted from the native character-preview
/// constructor and bone-update paths. Names refer to GRN helpers, not FX item filenames.</summary>
public sealed record SacredModelFxAttachmentDefinition(uint NativeConstructorAddress,
    SacredModelEffectKind EquipmentEffectKind, string StartBoneName, string? EndBoneName,
    uint NativeAddress);

public static class SacredModelFxAttachmentCatalogue
{
    public static IReadOnlyList<SacredModelFxAttachmentDefinition> Definitions =>
        EmbeddedModelFxAttachments.Definitions;

    public static SacredModelFxAttachmentDefinition? Find(SacredParticleDefinition definition) =>
        Definitions.FirstOrDefault(rule => rule.NativeConstructorAddress == definition.NativeConstructorAddress);
}
