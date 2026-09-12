using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Models;

/// <summary>
/// Common 0x100-byte header used by Sacred's descriptor-table PAK archives.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct ModelsPakHeaderLayout
{
    /// <summary>Serialized header size before the descriptor table.</summary>
    public const int SerializedSize = 0x100;

    /// <summary>Number of entry descriptors following the header.</summary>
    [FieldOffset(0x04)]
    public readonly uint EntryCount;
}