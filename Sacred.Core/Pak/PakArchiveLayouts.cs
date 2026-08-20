using System.Runtime.InteropServices;

namespace Sacred.Core.Pak;

/// <summary>
/// Common 0x100-byte header used by Sacred's descriptor-table PAK archives.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct PakArchiveHeaderLayout
{
    /// <summary>Serialized header size before the descriptor table.</summary>
    public const int SerializedSize = 0x100;

    /// <summary>Number of entry descriptors following the header.</summary>
    [FieldOffset(0x04)]
    public readonly uint EntryCount;
}

/// <summary>
/// One entry in the descriptor table shared by several Sacred PAK archives.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct PakEntryDescriptorLayout
{
    /// <summary>Serialized size of one descriptor.</summary>
    public const int SerializedSize = 0x0C;

    /// <summary>Archive-specific entry type or identifier.</summary>
    [FieldOffset(0x00)]
    public readonly uint Type;

    /// <summary>Absolute byte offset of the entry payload.</summary>
    [FieldOffset(0x04)]
    public readonly uint Offset;

    /// <summary>Payload size in bytes, excluding any format-specific prefix.</summary>
    [FieldOffset(0x08)]
    public readonly uint Size;
}
