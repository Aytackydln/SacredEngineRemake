using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero;

internal static class SacredHeroPaxDirectoryReader
{
    public static SacredHeroPaxChunkLayout[] Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SacredHeroPaxHeaderLayout.Size)
            throw new InvalidDataException("Truncated AMH hero header.");
        var header = MemoryMarshal.Read<SacredHeroPaxHeaderLayout>(payload);
        if (header.MarkerA != 'A' || header.MarkerM != 'M' || header.MarkerH != 'H')
            throw new InvalidDataException("Unsupported hero container marker.");
        if (header.ChunkCount > SacredHeroPaxHeaderLayout.DirectoryCapacity)
            throw new InvalidDataException("Hero chunk directory exceeds the native loader's sixteen-entry buffer.");
        var count = (int)header.ChunkCount;
        var directoryEnd = SacredHeroPaxHeaderLayout.Size + count * SacredHeroPaxChunkLayout.Size;
        if (payload.Length < directoryEnd) throw new InvalidDataException("Truncated hero chunk directory.");
        return MemoryMarshal.Cast<byte, SacredHeroPaxChunkLayout>(payload[SacredHeroPaxHeaderLayout.Size..directoryEnd]).ToArray();
    }
}
