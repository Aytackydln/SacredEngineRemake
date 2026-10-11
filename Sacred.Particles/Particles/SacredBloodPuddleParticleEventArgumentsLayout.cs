using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Mapped arguments consumed by Gold Blood Puddle initializer 7A5C30.
/// Full event size and the preceding fields remain unrecovered.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MappedSize)]
public readonly struct SacredBloodPuddleParticleEventArgumentsLayout
{
    public const int MappedSize = 0x40;
    [FieldOffset(0x00), BinaryUnknown] public readonly ulong Unknown000;
    [FieldOffset(0x08), BinaryUnknown] public readonly ulong Unknown008;
    [FieldOffset(0x10), BinaryUnknown] public readonly ulong Unknown010;
    [FieldOffset(0x18), BinaryUnknown] public readonly ulong Unknown018;
    [FieldOffset(0x20), BinaryUnknown] public readonly ulong Unknown020;
    [FieldOffset(0x28), BinaryUnknown] public readonly ulong Unknown028;
    [FieldOffset(0x30), BinaryUnknown] public readonly ulong Unknown030;
    /// <summary>Unsigned native half-range in both horizontal emission coordinates.</summary>
    [FieldOffset(0x38)] public readonly uint HorizontalExtent;
    /// <summary>RGB channels form the palette; the high alpha byte is ignored.</summary>
    [FieldOffset(0x3C)] public readonly uint Color;
}
