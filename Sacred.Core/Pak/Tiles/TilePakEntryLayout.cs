using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Tiles;

[InlineArray(15)]
public struct TilePakReservedBytes15
{
    private byte _element0;
}

/// <summary>
/// One native <c>cPatchSharedIso</c> definition stored in <c>tiles.pak</c>.
/// The descriptor payload is 64 bytes in the Demo and Gold archives.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct TilePakEntryLayout
{
    public const int SerializedSize = 0x40;

    /// <summary>Null-terminated tile resource name encoded as ASCII.</summary>
    [FieldOffset(0x00)]
    [BinaryString("ResourceName", 0x20, "ASCII")]
    private readonly byte _resourceName;

    /// <summary>Native texture: identifier of the source texture entry.</summary>
    [FieldOffset(0x20)]
    public readonly uint TextureId;

    /// <summary>Native subtexture: texture-region index used by this tile.</summary>
    [FieldOffset(0x24)]
    public readonly ushort SubtextureId;

    /// <summary>Tile number used by WLDX and floor-overlay references; aliases the native subtexture field.</summary>
    [FieldOffset(0x24)]
    public readonly ushort TileNumber;

    [FieldOffset(0x26), BinaryUnknown]
    public readonly ushort Reserved1;

    /// <summary>Native flags word. Individual bit meanings have not been established.</summary>
    [FieldOffset(0x28)]
    public readonly uint Flags;

    /// <summary>Native type selector. No original enum was emitted for this UInt16 field.</summary>
    [FieldOffset(0x2C)]
    public readonly ushort Type;

    /// <summary>Native frames count.</summary>
    [FieldOffset(0x2E)]
    public readonly byte Frames;
    /// <summary>Native frame1 selector.</summary>
    [FieldOffset(0x2F)]
    public readonly byte Frame1;
    /// <summary>Native frame2 selector.</summary>
    [FieldOffset(0x30)]
    public readonly byte Frame2;

    [FieldOffset(0x31), BinaryUnknown]
    public readonly TilePakReservedBytes15 Reserved2;
}
