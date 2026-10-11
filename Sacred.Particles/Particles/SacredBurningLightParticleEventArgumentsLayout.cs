using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Recovered event fields; extent is the mapped prefix, not the complete native event size.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MappedSize)]
public readonly struct SacredBurningLightParticleEventArgumentsLayout
{
    public const int MappedSize = 0x78;
    [FieldOffset(0x000), BinaryUnknown] public readonly ulong Unknown000;
    [FieldOffset(0x008), BinaryUnknown] public readonly uint Unknown008;
    [FieldOffset(0x00C)] public readonly uint OwnerIdentity;
    [FieldOffset(0x010), BinaryUnknown] public readonly ulong Unknown010;
    [FieldOffset(0x018), BinaryUnknown] public readonly ulong Unknown018;
    [FieldOffset(0x020), BinaryUnknown] public readonly ulong Unknown020;
    [FieldOffset(0x028), BinaryUnknown] public readonly ulong Unknown028;
    [FieldOffset(0x030), BinaryUnknown] public readonly ulong Unknown030;
    [FieldOffset(0x038), BinaryUnknown] public readonly ulong Unknown038;
    [FieldOffset(0x040)] public readonly ushort CombatArtLevel;
    [FieldOffset(0x042), BinaryUnknown] public readonly ushort Unknown042;
    [FieldOffset(0x044)] public readonly uint FallbackDurationSeconds;
    [FieldOffset(0x048), BinaryUnknown] public readonly ulong Unknown048;
    [FieldOffset(0x050), BinaryUnknown] public readonly ulong Unknown050;
    [FieldOffset(0x058), BinaryUnknown] public readonly ulong Unknown058;
    [FieldOffset(0x060), BinaryUnknown] public readonly ulong Unknown060;
    [FieldOffset(0x068), BinaryUnknown] public readonly ulong Unknown068;
    [FieldOffset(0x070)] public readonly float DurationMultiplier;
    [FieldOffset(0x074)] public readonly uint CombatArtParametersAddress;
}
