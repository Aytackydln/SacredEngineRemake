namespace Sacred.Particles;

/// <summary>Recovered stdCreationOnLine arguments. Attachment indices address
/// base-system points at +0x38, not Granny skeleton bone indices.</summary>
public sealed record SacredParticleLineEmissionDefinition(int StartAttachment, int EndAttachment,
    int SelectionMode, uint NativeAddress);
