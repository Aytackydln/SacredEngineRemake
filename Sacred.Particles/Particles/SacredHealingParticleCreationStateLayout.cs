using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Base-object flag: zero selects red quad corners; nonzero selects blue.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredHealingParticleCreationStateLayout
{
    public const int SerializedSize = 0xAA;
    [FieldOffset(0xA9)] public byte VariantFlag;
}
