using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>High Speed event fields consumed by 79AB70; only the selector's low byte is tested.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MappedSize)]
public readonly struct SacredHighSpeedParticleEventArgumentsLayout
{
    public const int MappedSize = 0x78;
    [FieldOffset(0x00), BinaryUnknown] public readonly ulong Unknown00;
    [FieldOffset(0x08), BinaryUnknown] public readonly ulong Unknown08;
    [FieldOffset(0x10), BinaryUnknown] public readonly ulong Unknown10;
    [FieldOffset(0x18), BinaryUnknown] public readonly ulong Unknown18;
    [FieldOffset(0x20), BinaryUnknown] public readonly ulong Unknown20;
    [FieldOffset(0x28), BinaryUnknown] public readonly ulong Unknown28;
    [FieldOffset(0x30), BinaryUnknown] public readonly ulong Unknown30;
    [FieldOffset(0x38)] public readonly byte Alternate;
    [FieldOffset(0x39), BinaryUnknown] public readonly byte Unknown39;
    [FieldOffset(0x3A), BinaryUnknown] public readonly ushort Unknown3A;
    [FieldOffset(0x3C), BinaryUnknown] public readonly uint Unknown3C;
    [FieldOffset(0x40)] public readonly ushort CombatArtLevel;
    [FieldOffset(0x42), BinaryUnknown] public readonly ushort Unknown42;
    [FieldOffset(0x44), BinaryUnknown] public readonly uint Unknown44;
    [FieldOffset(0x48), BinaryUnknown] public readonly ulong Unknown48;
    [FieldOffset(0x50), BinaryUnknown] public readonly ulong Unknown50;
    [FieldOffset(0x58), BinaryUnknown] public readonly ulong Unknown58;
    [FieldOffset(0x60), BinaryUnknown] public readonly ulong Unknown60;
    [FieldOffset(0x68), BinaryUnknown] public readonly ulong Unknown68;
    [FieldOffset(0x70)] public readonly float DurationMultiplier;
    [FieldOffset(0x74)] public readonly uint CombatArtParametersAddress;
}

