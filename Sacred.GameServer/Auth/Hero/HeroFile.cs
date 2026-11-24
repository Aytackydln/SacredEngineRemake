using System.Buffers.Binary;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Auth.Hero;

/// <summary>
/// The lobby's Closed Net character block and the AMH hero file inside it (Sacred.Core <c>SacredHeroPaxArchive</c> has the
/// verified layouts; this is the writing side, on caller buffers).
/// <list type="bullet">
/// <item>Block: 556-byte preview (a copy of chunk C3) | u32 zlib size | u32 file size | zlib(hero file).</item>
/// <item>File: 256-byte header ("AMH", object format version, u32 chunk count, ...), then the chunk directory at 0x100 of
/// {u32 type, u32 file offset, u32 decoded size} and the chunk data. Compressed chunks (C7, C8, CA, CB, CD) are a 32-byte header
/// (marker 0xBAADC0DE, u32 zlib size, zeros) followed by the zlib stream; C3, C4 and CE are raw.</item>
/// </list>
/// A rebuilt file keeps the header, the directory order and every chunk that is not replaced byte for byte.
/// </summary>
public static class HeroFile
{
    public const int PreviewBytes = 556, BlockHeaderBytes = PreviewBytes + 8, HeaderBytes = 256, DirectoryEntryBytes = 12;
    public const int CompressionHeaderBytes = 32, MaxChunks = 16;
    public const uint CompressionMarker = 0xBAADC0DE;
    public const uint Hero = 0xC0, Inventory = 0xC1, Objects = 0xC2, Profile = 0xC3, SharedInventory = 0xC6,
        CompressedHero = 0xC7, CompressedObjects = 0xC8, CompressedInventory = 0xCA, CompressedSharedInventory = 0xCB,
        StatisticsExtension = 0xCE;

    public static uint DecodedType(uint type) => type switch
    {
        CompressedHero => Hero, CompressedObjects => Objects, CompressedInventory => Inventory,
        CompressedSharedInventory => SharedInventory, 0xCD => 0xCC, _ => type,
    };

    public static bool IsCompressed(uint type) => DecodedType(type) != type;

    /// <summary>Inflates the hero file of a lobby block. Returns its length, or -1 when the block is malformed.</summary>
    public static int ReadBlock(ReadOnlySpan<byte> block, ZlibInflater inflater, Span<byte> file)
    {
        if (block.Length < BlockHeaderBytes) return -1;
        var compressed = BinaryPrimitives.ReadUInt32LittleEndian(block[PreviewBytes..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(block[(PreviewBytes + 4)..]);
        if (compressed > block.Length - BlockHeaderBytes || size > file.Length) return -1;
        if (!inflater.TryInflate(block.Slice(BlockHeaderBytes, (int)compressed), file[..(int)size], out var written) || written != size) return -1;
        return written;
    }

    /// <summary>Writes a lobby block for <paramref name="file"/> (preview = its C3 chunk). Returns the length, or -1.</summary>
    public static int WriteBlock(ReadOnlySpan<byte> file, ZlibDeflater deflater, Span<byte> block)
    {
        var preview = FindChunk(file, Profile, out var offset, out _);
        if (preview < 0 || offset + PreviewBytes > file.Length || block.Length < BlockHeaderBytes + 16) return -1;
        file.Slice(offset, PreviewBytes).CopyTo(block);
        var compressed = deflater.Compress(file, block[BlockHeaderBytes..]);
        if (compressed < 0) return -1;
        BinaryPrimitives.WriteUInt32LittleEndian(block[PreviewBytes..], (uint)compressed);
        BinaryPrimitives.WriteUInt32LittleEndian(block[(PreviewBytes + 4)..], (uint)file.Length);
        return BlockHeaderBytes + compressed;
    }

    public static int ChunkCount(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderBytes || file[0] != 'A' || file[1] != 'M' || file[2] != 'H') return -1;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]);
        return count > MaxChunks || HeaderBytes + count * DirectoryEntryBytes > file.Length ? -1 : (int)count;
    }

    public static (uint Type, uint Offset, uint Size) Entry(ReadOnlySpan<byte> file, int index)
    {
        var e = file[(HeaderBytes + index * DirectoryEntryBytes)..];
        return (BinaryPrimitives.ReadUInt32LittleEndian(e), BinaryPrimitives.ReadUInt32LittleEndian(e[4..]), BinaryPrimitives.ReadUInt32LittleEndian(e[8..]));
    }

    /// <summary>Index of the last chunk whose (decoded) type is <paramref name="decodedType"/> (the native loader lets the last one win).</summary>
    public static int FindChunk(ReadOnlySpan<byte> file, uint decodedType, out int offset, out uint type)
    {
        offset = 0; type = 0;
        var count = ChunkCount(file);
        var found = -1;
        for (var i = 0; i < count; i++)
        {
            var e = Entry(file, i);
            if (e.Type == 0 || DecodedType(e.Type) != decodedType) continue;
            found = i; offset = (int)e.Offset; type = e.Type;
        }
        return found;
    }

    /// <summary>File extent of chunk <paramref name="index"/>: up to the next chunk's data, or the end of the file.</summary>
    public static int Extent(ReadOnlySpan<byte> file, int index)
    {
        var count = ChunkCount(file);
        var start = Entry(file, index).Offset;
        var end = (uint)file.Length;
        for (var i = 0; i < count; i++)
        {
            var e = Entry(file, i);
            if (e.Type != 0 && e.Offset > start && e.Offset < end) end = e.Offset;
        }
        return (int)(end - start);
    }

    /// <summary>Decodes chunk <paramref name="index"/> into <paramref name="output"/>; returns the length or -1.</summary>
    public static int Decode(ReadOnlySpan<byte> file, int index, ZlibInflater inflater, Span<byte> output)
    {
        var e = Entry(file, index);
        var extent = Extent(file, index);
        if (e.Offset + (ulong)extent > (ulong)file.Length) return -1;
        var data = file.Slice((int)e.Offset, extent);
        if (!IsCompressed(e.Type))
        {
            var raw = Math.Min(extent, (int)Math.Min(e.Size, (uint)extent));
            if (raw > output.Length) return -1;
            data[..raw].CopyTo(output);
            return raw;
        }
        if (data.Length < CompressionHeaderBytes) return -1;
        var compressed = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (compressed > data.Length - CompressionHeaderBytes || e.Size > output.Length) return -1;
        return inflater.TryInflate(data.Slice(CompressionHeaderBytes, (int)compressed), output[..(int)e.Size], out var written) ? written : -1;
    }

    /// <summary>
    /// Rebuilds <paramref name="file"/> into <paramref name="output"/>, replacing the decoded content of the chunks listed in
    /// <paramref name="replacements"/> (index -> new decoded bytes; compressed chunks are compressed again). Everything else is
    /// copied verbatim, chunk data keeps its order. Returns the new length or -1.
    /// </summary>
    public static int Rebuild(ReadOnlySpan<byte> file, ReadOnlySpan<Replacement> replacements, ZlibDeflater deflater, Span<byte> output)
    {
        var count = ChunkCount(file);
        if (count < 0) return -1;
        var dataStart = HeaderBytes + count * DirectoryEntryBytes;
        for (var i = 0; i < count; i++) { var e = Entry(file, i); if (e.Type != 0 && e.Offset < dataStart) return -1; }
        if (output.Length < dataStart) return -1;
        file[..dataStart].CopyTo(output);
        // Chunks in file order (the original writes them in directory order, but keep whatever order the file has).
        Span<int> order = stackalloc int[MaxChunks];
        var used = 0;
        for (var i = 0; i < count; i++) if (Entry(file, i).Type != 0) order[used++] = i;
        order = order[..used];
        for (var a = 1; a < order.Length; a++)
            for (var b = a; b > 0 && Entry(file, order[b]).Offset < Entry(file, order[b - 1]).Offset; b--) (order[b], order[b - 1]) = (order[b - 1], order[b]);
        var position = dataStart;
        if (order.Length > 0 && Entry(file, order[0]).Offset > dataStart)
        {
            var gap = (int)Entry(file, order[0]).Offset - dataStart;     // keep any bytes between directory and the first chunk
            if (position + gap > output.Length) return -1;
            file.Slice(dataStart, gap).CopyTo(output[position..]);
            position += gap;
        }
        foreach (var index in order)
        {
            var e = Entry(file, index);
            var entry = output[(HeaderBytes + index * DirectoryEntryBytes)..];
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)position);
            var replacement = -1;
            for (var r = 0; r < replacements.Length; r++) if (replacements[r].Index == index) replacement = r;
            if (replacement < 0)
            {
                var extent = Extent(file, index);
                if (position + extent > output.Length) return -1;
                file.Slice((int)e.Offset, extent).CopyTo(output[position..]);
                position += extent;
                continue;
            }
            var content = replacements[replacement].Content.Span;
            if (!IsCompressed(e.Type))
            {
                if (position + content.Length > output.Length) return -1;
                content.CopyTo(output[position..]);
                position += content.Length;
                if (e.Size == Extent(file, index)) BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)content.Length);
                continue;
            }
            if (position + CompressionHeaderBytes > output.Length) return -1;
            var header = output.Slice(position, CompressionHeaderBytes);
            header.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(header, CompressionMarker);
            var compressed = deflater.Compress(content, output[(position + CompressionHeaderBytes)..]);
            if (compressed < 0) return -1;
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)compressed);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)content.Length);
            position += CompressionHeaderBytes + compressed;
        }
        return position;
    }

    public readonly record struct Replacement(int Index, ReadOnlyMemory<byte> Content);
}
