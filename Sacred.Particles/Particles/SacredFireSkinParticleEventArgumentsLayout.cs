using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Recovered event fields; extent is the mapped prefix, not the complete native event size.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MappedSize)]
public readonly struct SacredFireSkinParticleEventArgumentsLayout
{
    public const int MappedSize = 0x78;
    [FieldOffset(0x000), BinaryUnknown] public readonly ulong Unknown000;
    [FieldOffset(0x008), BinaryUnknown] public readonly ulong Unknown008;
    [FieldOffset(0x010), BinaryUnknown] public readonly ulong Unknown010;
    [FieldOffset(0x018), BinaryUnknown] public readonly ulong Unknown018;
    [FieldOffset(0x020)] public readonly SacredParticleVectorLayout Position;
    [FieldOffset(0x02C), BinaryUnknown] public readonly ulong Unknown02C;
    [FieldOffset(0x034), BinaryUnknown] public readonly ulong Unknown034;
    [FieldOffset(0x03C), BinaryUnknown] public readonly uint Unknown03C;
    [FieldOffset(0x040)] public readonly ushort CombatArtLevel;
    [FieldOffset(0x042), BinaryUnknown] public readonly ulong Unknown042;
    [FieldOffset(0x04A), BinaryUnknown] public readonly ulong Unknown04A;
    [FieldOffset(0x052), BinaryUnknown] public readonly ulong Unknown052;
    [FieldOffset(0x05A), BinaryUnknown] public readonly ulong Unknown05A;
    [FieldOffset(0x062), BinaryUnknown] public readonly ulong Unknown062;
    [FieldOffset(0x06A), BinaryUnknown] public readonly uint Unknown06A;
    [FieldOffset(0x06E), BinaryUnknown] public readonly ushort Unknown06E;
    [FieldOffset(0x070)] public readonly float DurationMultiplier;
    [FieldOffset(0x074)] public readonly uint CombatArtParametersAddress;
}
