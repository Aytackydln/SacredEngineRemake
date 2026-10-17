using System.Buffers.Binary;

namespace Sacred.Core.Pax.Hero.Statistics;

/// <summary>Lossless statistics extension shared by PAX 0xCE and Gold 0x14F. Individual word meanings remain unknown.</summary>
public sealed class SacredHeroStatisticsExtension
{
    private readonly byte[] _payload;
    private SacredHeroStatisticsExtension(byte[] payload) => _payload = payload;
    public ReadOnlySpan<byte> Data => _payload;

    public uint GetWord(int index)
    {
        if ((uint)index >= SacredHeroStatisticsExtensionLayout.WordCount) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(index * SacredHeroStatisticsExtensionLayout.WordSize));
    }

    public static SacredHeroStatisticsExtension FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != SacredHeroStatisticsExtensionLayout.Size)
            throw new InvalidDataException("Gold hero statistics extension requires exactly 100 bytes.");
        return new(payload.ToArray());
    }

    public byte[] ToPayload() => _payload.ToArray();
}
