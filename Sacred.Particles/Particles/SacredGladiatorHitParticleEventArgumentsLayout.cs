using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Mapped Gladiator hit initializer input at event +3C (78F1C9).
/// Full event extent and preceding fields remain unrecovered.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MappedSize)]
public readonly struct SacredGladiatorHitParticleEventArgumentsLayout
{
    public const int MappedSize = 0x40;
    [FieldOffset(0x00), BinaryUnknown] public readonly ulong Unknown000;
    [FieldOffset(0x08), BinaryUnknown] public readonly ulong Unknown008;
    [FieldOffset(0x10), BinaryUnknown] public readonly ulong Unknown010;
    [FieldOffset(0x18), BinaryUnknown] public readonly ulong Unknown018;
    [FieldOffset(0x20), BinaryUnknown] public readonly ulong Unknown020;
    [FieldOffset(0x28), BinaryUnknown] public readonly ulong Unknown028;
    [FieldOffset(0x30), BinaryUnknown] public readonly ulong Unknown030;
    [FieldOffset(0x38), BinaryUnknown] public readonly uint Unknown038;
    /// <summary>Compared to the native special combat-art selector; all other values share a branch.</summary>
    [FieldOffset(0x3C)] public readonly uint CombatArtType;
}
