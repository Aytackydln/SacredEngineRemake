using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Verified initializer input in the base object; zero selects white, nonzero selects pink.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredShieldedFxCreationStateLayout
{
    public const int SerializedSize = 0xAA;
    [FieldOffset(0xA9)] public byte VariantFlag;
}
