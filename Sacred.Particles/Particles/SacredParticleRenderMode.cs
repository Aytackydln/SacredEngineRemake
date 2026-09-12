namespace Sacred.Particles.Particles;

/// <summary>
/// Native <c>PS_RENDERMODE_*</c> flags passed to <c>cParticleSystem::stdRender</c>.
/// These flags, rather than Texture.pak metadata, select particle colour,
/// lighting, orientation, and additive rendering behavior.
/// </summary>
[Flags]
public enum SacredParticleRenderMode : uint
{
    None = 0,

    /// <summary>Native <c>PS_RENDERMODE_ADDITIVE</c>.</summary>
    Additive = 1u << 0,

    /// <summary>Native <c>ENERGY_ALPHA</c>.</summary>
    EnergyAlpha = 1u << 1,

    /// <summary>Native <c>PHI</c>.</summary>
    Phi = 1u << 2,

    /// <summary>Native <c>ENERGY_COL</c>.</summary>
    EnergyColor = 1u << 3,

    /// <summary>Native <c>ADDITIVE_COLOR</c>.</summary>
    AdditiveColor = 1u << 4,

    /// <summary>Native <c>PARTICLE_COL</c>.</summary>
    ParticleColor = 1u << 5,

    /// <summary>Native <c>USE_WORLD_LIGHT</c>.</summary>
    UseWorldLight = 1u << 6,

    /// <summary>Native <c>CENTER_AT_DOWN</c>.</summary>
    CenterAtDown = 1u << 7,

    /// <summary>Native <c>MULTIPART</c>.</summary>
    Multipart = 1u << 8
}
