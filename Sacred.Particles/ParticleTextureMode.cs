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
    WeaponGlowFlare = 8
}
