using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Base-object flag: zero selects Fire02 and combat duration; nonzero selects Fire09 and three seconds.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredFireCircleParticleCreationStateLayout
{
    public const int SerializedSize = 0xAA;
    [FieldOffset(0xA9)] public byte VariantFlag;
}
