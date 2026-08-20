using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Mixed;

/// <summary>Binary layout of one 0x40-byte mixed.pak sprite piece.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct MixedPakPieceLayout
{
    public const int SerializedSize = 0x40;

    /// <summary>Null-terminated sprite-piece resource name encoded as ASCII.</summary>
    [FieldOffset(0x00)]
    [BinaryString("ResourceName", 0x20, "ASCII")]
    private readonly byte _resourceName;

    /// <summary>Texture cutout identifier used by the sprite piece.</summary>
    [FieldOffset(0x20)] public readonly uint CutoutId;
    /// <summary>Right edge of the source rectangle.</summary>
    [FieldOffset(0x24)] public readonly ushort Right;
    /// <summary>Bottom edge of the source rectangle.</summary>
    [FieldOffset(0x26)] public readonly ushort Bottom;
    /// <summary>Left edge of the source rectangle.</summary>
    [FieldOffset(0x28)] public readonly short Left;
    /// <summary>Top edge of the source rectangle.</summary>
    [FieldOffset(0x2A)] public readonly short Top;
    /// <summary>Unresolved value at byte offset 0x2C.</summary>
    [FieldOffset(0x2C)] public readonly uint Unknown2C;
    /// <summary>First normalized texture-coordinate component.</summary>
    [FieldOffset(0x30)] public readonly float Uv0;
    /// <summary>Second normalized texture-coordinate component.</summary>
    [FieldOffset(0x34)] public readonly float Uv1;
    /// <summary>Third normalized texture-coordinate component.</summary>
    [FieldOffset(0x38)] public readonly float Uv2;
    /// <summary>Fourth normalized texture-coordinate component.</summary>
    [FieldOffset(0x3C)] public readonly float Uv3;
}
