using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Native _sFile_chunk, used by sector metadata and height-level descriptors.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct WorldFileChunkLayout
{
    public const int SerializedSize = 12;
    [FieldOffset(0)] public readonly uint Type;
    [FieldOffset(4)] public readonly uint Position;
    /// <summary>Native size; some chunk kinds store an entry count rather than a byte length.</summary>
    [FieldOffset(8)] public readonly uint Size;
}
