using Sacred.Particles.Generated;

namespace Sacred.Particles;

/// <summary>Native cObjectFX attachment branch, extracted independently of particle resource IDs.</summary>
public sealed record SacredFxAttachmentDefinition(uint FxSubtype, ushort Bone, bool BoneFromSelector,
    uint NativeAddress);

public static class SacredFxAttachments
{
    public static IReadOnlyList<SacredFxAttachmentDefinition> Definitions => EmbeddedFxAttachments.Definitions;
    public static SacredFxAttachmentDefinition? Find(uint subtype) => Definitions.FirstOrDefault(d => d.FxSubtype == subtype);
    public static string? BoneName(uint bone) => bone < EmbeddedFxAttachments.BoneNames.Count
        ? EmbeddedFxAttachments.BoneNames[(int)bone] : null;
}
