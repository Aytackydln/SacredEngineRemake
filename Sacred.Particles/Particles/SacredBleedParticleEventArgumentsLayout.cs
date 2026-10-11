using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Mapped Bleed initializer inputs at event +38/+3C; full native event extent remains unknown.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MappedSize)]
public readonly struct SacredBleedParticleEventArgumentsLayout
{
    public const int MappedSize = 0x40;
    [FieldOffset(0x00), BinaryUnknown] public readonly ulong Unknown00;
    [FieldOffset(0x08), BinaryUnknown] public readonly ulong Unknown08;
    [FieldOffset(0x10), BinaryUnknown] public readonly ulong Unknown10;
    [FieldOffset(0x18), BinaryUnknown] public readonly ulong Unknown18;
    [FieldOffset(0x20), BinaryUnknown] public readonly ulong Unknown20;
    [FieldOffset(0x28), BinaryUnknown] public readonly ulong Unknown28;
    [FieldOffset(0x30), BinaryUnknown] public readonly ulong Unknown30;
    [FieldOffset(0x38)] public readonly uint Color;
    [FieldOffset(0x3C)] public readonly uint BloodType;
}

