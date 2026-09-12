namespace Sacred.Particles;

/// <summary>
/// Animation/composition modes used by Sacred's model-attached particle effects.
/// The numeric values are passed to the particle shaders and are therefore stable.
/// </summary>
public enum ParticleTextureMode
{
    Luminance = 1,
    Atlas4X4 = 2,
    Alpha = 3,
    BouncyAlpha = 4,
    MagicOrb = 5,
    FirePop = 6,
    PoisonStatic = 7,
    WeaponGlowFlare = 8,
    /// <summary>CPU simulated native model particle; shader uses supplied size, UV and color.</summary>
    NativeModel = 9,
    /// <summary>Native quad with RGB packed as an exact integer plus one in Normal.Z.</summary>
    NativeModelColored = 10,
    /// <summary>Non-rotating native stdLensflare whose RGB texture has no coverage alpha.</summary>
    NativeLensFlare = 11
}
