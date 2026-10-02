namespace Sacred.Particles;

/// <summary>Literal stdLensflare billboard accompanying a particle system.</summary>
public sealed record SacredParticleHaloDefinition(string TextureName, float HalfSize, uint Color,
    uint NativeAddress);
