using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold generic FX payload at object +0x20A0, serialized by 0x796C60.
/// Event +0x38 supplies a packed color; event +0x3C selects the subtype.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredGenericParticleStateLayout
{
    public const int SerializedSize = 0x4C8;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
    // Auxiliary helix/arrow state belongs to other, still unsupported subtypes.
    [FieldOffset(0x48C), BinaryUnknown] public readonly SacredGenericParticleAuxiliaryStateLayout Unknown48C;
    [FieldOffset(0x4C4)] public readonly byte EventSubtype;
    [FieldOffset(0x4C5), BinaryUnknown] public readonly byte Unknown4C5;
    [FieldOffset(0x4C6), BinaryUnknown] public readonly ushort Unknown4C6;
}

[InlineArray(0x38)]
public struct SacredGenericParticleAuxiliaryStateLayout
{
    private byte _element0;
}
