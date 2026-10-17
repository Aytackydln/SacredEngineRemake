using System.Runtime.InteropServices;
using Sacred.Core.Pax.Hero.Statistics;

namespace Sacred.Core.Pax.Hero;

/// <summary>Read-only Gold AMH directory and bounded chunk decoding. Unknown chunks and original file bytes remain intact.</summary>
public sealed class SacredHeroPaxArchive
{
    private readonly byte[] _payload;
    private readonly SacredHeroPaxChunkLayout[] _chunks;
    private SacredHeroPaxArchive(byte[] payload, SacredHeroPaxChunkLayout[] chunks) => (_payload, _chunks) = (payload, chunks);
    public SacredHeroPaxHeaderLayout Header => MemoryMarshal.Read<SacredHeroPaxHeaderLayout>(_payload);
    public ReadOnlySpan<SacredHeroPaxChunkLayout> Chunks => _chunks;
    public ReadOnlySpan<byte> HeaderBytes => _payload.AsSpan(0, SacredHeroPaxHeaderLayout.Size);
    public byte[] ToPayload() => _payload.ToArray();
    public static SacredHeroPaxArchive Read(string path) => FromPayload(File.ReadAllBytes(path));
    public static SacredHeroPaxArchive ReadSelected(string gameDirectory, int selection) => Read(SacredHeroPaxFilePath.ForSelection(gameDirectory, selection));
    public static SacredHeroPaxArchive FromPayload(ReadOnlySpan<byte> payload)
    {
        var chunks = SacredHeroPaxDirectoryReader.Read(payload);
        return new(payload.ToArray(), chunks);
    }

    /// <summary>The caller's decoded byte budget is a managed allocation limit, not a recovered native restriction.</summary>
    public byte[] DecodeChunk(int index, int maximumDecodedBytes)
    {
        if ((uint)index >= _chunks.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (maximumDecodedBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
        var chunk = _chunks[index];
        var type = SacredHeroPaxChunkTypes.GetDecodedType(chunk.Type);
        uint size = type switch
        {
            SacredHeroPaxChunkTypes.Profile => SacredHeroProfileLayout.Size,
            SacredHeroPaxChunkTypes.StartInfo => 64u,
            SacredHeroPaxChunkTypes.StatisticsExtension => SacredHeroStatisticsExtensionLayout.Size,
            SacredHeroPaxChunkTypes.Hero or SacredHeroPaxChunkTypes.Inventory or SacredHeroPaxChunkTypes.Objects or
                SacredHeroPaxChunkTypes.SharedInventory or SacredHeroPaxChunkTypes.Scripts => chunk.DecodedSize,
            _ => throw new NotSupportedException($"Hero chunk type 0x{chunk.Type:X} has no verified decoder.")
        };
        if (size > maximumDecodedBytes) throw new InvalidDataException("Hero chunk exceeds the caller's decoded byte budget.");
        if (type == chunk.Type) return Slice(chunk.FileOffset, size).ToArray();
        var header = MemoryMarshal.Read<SacredHeroPaxCompressionLayout>(Slice(chunk.FileOffset, SacredHeroPaxCompressionLayout.Size));
        var compressed = Slice((ulong)chunk.FileOffset + SacredHeroPaxCompressionLayout.Size, header.CompressedSize);
        return SacredHeroPaxDecompression.Decode(compressed, (int)size);
    }

    public SacredHeroPaxCompressionLayout GetCompressionHeader(int index)
    {
        if ((uint)index >= _chunks.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var chunk = _chunks[index];
        if (chunk.Type == SacredHeroPaxChunkTypes.GetDecodedType(chunk.Type))
            throw new InvalidOperationException("Chunk has no verified compressed wrapper.");
        return MemoryMarshal.Read<SacredHeroPaxCompressionLayout>(Slice(chunk.FileOffset, SacredHeroPaxCompressionLayout.Size));
    }

    private ReadOnlySpan<byte> Slice(ulong offset, ulong size)
    {
        if (offset > (ulong)_payload.Length || size > (ulong)_payload.Length - offset)
            throw new InvalidDataException("Truncated hero chunk.");
        return _payload.AsSpan((int)offset, (int)size);
    }
}
