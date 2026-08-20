using System.Runtime.InteropServices;

namespace Sacred.Core.World.Stairs;

/// <summary>One trigger-cell to zone-anchor association in <c>treppe.bin</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredStairsCellLayout
{
    /// <summary>Serialized size of one stairs-cell association.</summary>
    public const int SerializedSize = sizeof(uint) * 2;

    /// <summary>Packed X/Y/surface coordinate of the trigger cell.</summary>
    [FieldOffset(0x00)] public readonly uint PackedPosition;

    /// <summary>Packed X/Y/surface coordinate identifying the stairs zone.</summary>
    [FieldOffset(0x04)] public readonly uint PackedAnchor;
}
