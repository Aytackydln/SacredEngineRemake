using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Recovered fields of the original 32-bit cParticleSystem_dwarfmagic
/// object (constructor 0x78A2D0), including the blue lamp sparkle system.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDwarfMagicParticleSystemLayout
{
    public const int SerializedSize = 0x2530;
    [FieldOffset(0x0000)] public readonly uint NativeVtableAddress;
    [FieldOffset(0x20A0)] public readonly SacredDwarfMagicParticleStateLayout State;
    /// <summary>Resolved from PARTICLE_SPARK04.TGA by native name lookup.</summary>
    [FieldOffset(0x252C)] public readonly uint SparkTextureHandle;
}
