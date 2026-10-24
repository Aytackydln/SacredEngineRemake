using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>7E9ED0..7EA1F3 legacy public-profile normalization. Unmapped bytes remain owned and unchanged.</summary>
public sealed class GoldLobbyPublicProfile
{
    private readonly byte[] _bytes;
    private GoldLobbyPublicProfile(byte[] bytes, int originalSize) => (_bytes, OriginalSize) = (bytes, originalSize);
    public int OriginalSize { get; }
    public ReadOnlyMemory<byte> Bytes => _bytes;
    public uint Identity => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(0x662));
    public uint Word666 => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(0x666));
    public byte Byte66A => _bytes[0x66A];
    public byte Byte66B => _bytes[0x66B];
    public ReadOnlyMemory<byte> PlayerProfile => _bytes.AsMemory(0xB6C, 556);
    public static GoldLobbyPublicProfile? Read(ReadOnlySpan<byte> source)
    {
        var version = source.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(source) : 0;
        if (source.Length == 0xD98 && version == 4) return new(source.ToArray(), source.Length);
        if (source.Length != 0xBB2 && !(source.Length == 0xC54 && version == 2) && !(source.Length == 0xD94 && version == 3)) return null;
        var bytes = new byte[0xD98];
        var old = source.Length == 0xBB2;
        source.Slice(old ? 0 : 2, 0x5C0).CopyTo(bytes.AsSpan(2));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x662), BinaryPrimitives.ReadUInt32LittleEndian(source[(old ? 0x5C0 : 0x662)..]));
        bytes[0x66A] = source[old ? 0x5C4 : 0x666]; bytes[0x66B] = source[old ? 0x5C5 : 0x667];
        var extended = source.Length == 0xD94;
        source.Slice(old ? 0x5C6 : 0x668, extended ? 0x500 : 0x3C0).CopyTo(bytes.AsSpan(0x66C));
        source.Slice(old ? 0x986 : extended ? 0xB68 : 0xA28, 556).CopyTo(bytes.AsSpan(0xB6C));
        return new(bytes, source.Length);
    }
}
