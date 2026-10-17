using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero;

/// <summary>Read-only Gold hero profile extraction; object, inventory and script chunks are not imported.</summary>
public sealed class SacredHeroPaxProfile
{
    private readonly byte[] _header;
    private readonly SacredHeroPaxChunkLayout[] _chunks;
    private readonly byte[] _profile;

    private SacredHeroPaxProfile(byte[] header, SacredHeroPaxChunkLayout[] chunks, byte[] profile)
    {
        _header = header;
        _chunks = chunks;
        _profile = profile;
    }

    public SacredHeroPaxHeaderLayout Header => MemoryMarshal.Read<SacredHeroPaxHeaderLayout>(_header);
    public ReadOnlySpan<byte> HeaderBytes => _header;
    public ReadOnlySpan<SacredHeroPaxChunkLayout> Chunks => _chunks;
    public ReadOnlySpan<byte> Profile => _profile;
    public ushort Level => BinaryPrimitives.ReadUInt16LittleEndian(_profile.AsSpan(SacredHeroProfileLayout.LevelOffset));
    public ushort Word02 => BinaryPrimitives.ReadUInt16LittleEndian(_profile.AsSpan(SacredHeroProfileLayout.Word02Offset));
    public uint Word04 => BinaryPrimitives.ReadUInt32LittleEndian(_profile.AsSpan(SacredHeroProfileLayout.Word04Offset));
    public string Name
    {
        get
        {
            var name = _profile.AsSpan(SacredHeroProfileLayout.NameOffset);
            for (var offset = 0; offset + 2 <= name.Length; offset += 2)
                if (BinaryPrimitives.ReadUInt16LittleEndian(name[offset..]) == 0)
                {
                    var characters = new char[offset / 2];
                    for (var index = 0; index < characters.Length; index++)
                        characters[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(name[(index * 2)..]);
                    return new string(characters);
                }
            throw new InvalidDataException("Hero profile name has no terminator within the retained profile.");
        }
    }

    public static SacredHeroPaxProfile Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromPayload(File.ReadAllBytes(path));
    }

    /// <summary>Native 0x7F08A0 accepts selections 1..8 and reads Save/Hero(selection-1).pax.</summary>
    public static SacredHeroPaxProfile ReadSelected(string gameDirectory, int selection)
    {
        return Read(SacredHeroPaxFilePath.ForSelection(gameDirectory, selection));
    }

    public static SacredHeroPaxProfile FromPayload(ReadOnlySpan<byte> payload)
    {
        var chunks = SacredHeroPaxDirectoryReader.Read(payload);
        byte[]? profile = null;
        for (var index = 0; index < chunks.Length; index++)
        {
            var chunk = chunks[index];
            if (chunk.Type != SacredHeroProfileLayout.ChunkType) continue;
            if ((ulong)chunk.FileOffset + SacredHeroProfileLayout.Size > (ulong)payload.Length)
                throw new InvalidDataException("Truncated hero profile chunk.");
            // Native iteration overwrites the destination for every C3 entry; the final one wins.
            profile = payload.Slice((int)chunk.FileOffset, SacredHeroProfileLayout.Size).ToArray();
        }
        if (profile is null) throw new InvalidDataException("Hero container has no C3 admission profile.");
        return new(payload[..SacredHeroPaxHeaderLayout.Size].ToArray(), chunks, profile);
    }
}
