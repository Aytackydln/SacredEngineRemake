using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Recovered event fields; extent is the mapped prefix, not the complete native event size.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MappedSize)]
public readonly struct SacredGladiatorCastParticleEventArgumentsLayout
{
    public const int MappedSize = 0x3E;
    [FieldOffset(0x000), BinaryUnknown] public readonly ulong Unknown000;
    [FieldOffset(0x008), BinaryUnknown] public readonly ulong Unknown008;
    [FieldOffset(0x010), BinaryUnknown] public readonly ulong Unknown010;
    [FieldOffset(0x018), BinaryUnknown] public readonly ulong Unknown018;
    [FieldOffset(0x020), BinaryUnknown] public readonly ulong Unknown020;
    [FieldOffset(0x028), BinaryUnknown] public readonly ulong Unknown028;
    [FieldOffset(0x030), BinaryUnknown] public readonly ulong Unknown030;
    [FieldOffset(0x038), BinaryUnknown] public readonly uint Unknown038;
    [FieldOffset(0x03C)] public readonly ushort CombatArtType;
}
