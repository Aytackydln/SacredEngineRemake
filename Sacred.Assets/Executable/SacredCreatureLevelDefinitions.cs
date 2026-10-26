using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>SetLevel's explicit resource cap from original instructions. Neither the
/// resource nor its maximum is duplicated as a production item-ID exception.</summary>
public sealed class SacredCreatureLevelDefinitions
{
    private readonly uint _type;
    private readonly uint _maximum;
    private SacredCreatureLevelDefinitions(uint type, uint maximum) => (_type, _maximum) = (type, maximum);

    public uint? MaximumOverride(uint type) => type == _type ? _maximum : null;

    public static SacredCreatureLevelDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> prefix = [0x8B, 0x45, 4, 0x8B, 0x50, 0x10, 0x81, 0xFA];
        ReadOnlySpan<byte> branch = [0x75, 5, 0xBE];
        var found = -1;
        for (var offset = 0; offset <= image.Length - 19; offset++)
        {
            if (!image.Slice(offset, prefix.Length).SequenceEqual(prefix) ||
                !image.Slice(offset + 12, branch.Length).SequenceEqual(branch)) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original creature level cap.");
            found = offset;
        }
        if (found < 0) throw new InvalidDataException("Original creature level cap instructions unavailable.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(image[(found + 8)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(image[(found + 15)..]));
    }

    public static SacredCreatureLevelDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original creature level definitions unavailable.", failure);
    }
}
