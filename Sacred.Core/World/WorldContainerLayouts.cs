using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Header preceding sector records in <c>sectors.keyx</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct KeyxHeaderLayout
{
    /// <summary>Serialized header size.</summary>
    public const int SerializedSize = 0x100;

    /// <summary>Number of 0x300-byte sector records.</summary>
    [FieldOffset(0x04)] public readonly uint SectorCount;
}

/// <summary>
/// Descriptor for one indoor tile grid following the outdoor tile table in a
/// decompressed <c>sectors.wldx</c> sector payload.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct WldxIndoorGroupDescriptorLayout
{
    /// <summary>Serialized descriptor size.</summary>
    public const int SerializedSize = 0x24;

    /// <summary>World X coordinate of the indoor grid origin.</summary>
    [FieldOffset(0x00)] public readonly int WorldX;
    /// <summary>World Y coordinate of the indoor grid origin.</summary>
    [FieldOffset(0x04)] public readonly int WorldY;
    /// <summary>Indoor grid width in tiles.</summary>
    [FieldOffset(0x08)] public readonly ushort Width;
    /// <summary>Indoor grid height in tiles.</summary>
    [FieldOffset(0x0A)] public readonly ushort Height;
    /// <summary>Payload kind; observed indoor tile grids use value 6.</summary>
    [FieldOffset(0x0C)] public readonly uint Kind;
    /// <summary>Offset of the indoor tile array in the decompressed sector payload.</summary>
    [FieldOffset(0x10)] public readonly uint TilesOffset;
    /// <summary>Byte length of the indoor tile array.</summary>
    [FieldOffset(0x14)] public readonly uint TilesSize;
}

/// <summary>
/// Unresolved fixed-size block between the outdoor WLDX tile table and the
/// first indoor-grid descriptor.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct WldxPostTileHeaderLayout
{
    /// <summary>Serialized size of the unresolved block.</summary>
    public const int SerializedSize = 0x24;
}
