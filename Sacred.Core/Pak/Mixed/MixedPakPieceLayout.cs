using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Mixed;

/// <summary>Binary layout of one 0x40-byte mixed.pak sprite piece.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct MixedPakPieceLayout
{
    public const int SerializedSize = 0x40;

    [FieldOffset(0x20)] public readonly uint CutoutId;
    [FieldOffset(0x24)] public readonly ushort Right;
    [FieldOffset(0x26)] public readonly ushort Bottom;
    [FieldOffset(0x28)] public readonly short Left;
    [FieldOffset(0x2A)] public readonly short Top;
    [FieldOffset(0x2C)] public readonly uint Unknown2C;
    [FieldOffset(0x30)] public readonly float Uv0;
    [FieldOffset(0x34)] public readonly float Uv1;
    [FieldOffset(0x38)] public readonly float Uv2;
    [FieldOffset(0x3C)] public readonly float Uv3;
}
