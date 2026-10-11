using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Call of Death reads the radius from the native event at +2C.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x30)]
public struct SacredDeathCallFlareEventLayout
{
    [FieldOffset(0), BinaryUnknown] public uint Unknown000;
    [FieldOffset(4), BinaryUnknown] public uint Unknown004;
    [FieldOffset(8), BinaryUnknown] public uint Unknown008;
    [FieldOffset(12), BinaryUnknown] public uint Unknown00C;
    [FieldOffset(16), BinaryUnknown] public uint Unknown010;
    [FieldOffset(20), BinaryUnknown] public uint Unknown014;
    [FieldOffset(24), BinaryUnknown] public uint Unknown018;
    [FieldOffset(28), BinaryUnknown] public uint Unknown01C;
    [FieldOffset(32), BinaryUnknown] public uint Unknown020;
    [FieldOffset(36), BinaryUnknown] public uint Unknown024;
    [FieldOffset(40), BinaryUnknown] public uint Unknown028;
    [FieldOffset(0x2C)] public float Radius;
}
