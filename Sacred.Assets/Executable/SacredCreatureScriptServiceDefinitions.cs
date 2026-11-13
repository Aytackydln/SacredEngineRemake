using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Original CreateNPC merchant service initialization operands.
/// The dialog selector is read from executable code, not a fabricated resource.</summary>
public sealed record SacredCreatureScriptServiceDefinitions(uint HorseMerchantFlags, uint HorseMerchantDialog, uint DialogObjectFlags)
{
    public uint TraderFlags { get; init; }
    public uint ComboMasterFlags { get; init; }
    public uint SmithFlags { get; init; }
    public static SacredCreatureScriptServiceDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory,name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original NPC service initialization unavailable.",failure);
    }
    public static SacredCreatureScriptServiceDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> anchor = [0x8B,0x8F,0,2,0,0,0x8A,0x45,0x14,0x81,0xC9];
        var at = image.IndexOf(anchor);
        if (at < 0 || image[(at+1)..].IndexOf(anchor) >= 0)
            throw new InvalidDataException("Unique original HorseMerchant service initialization unavailable.");
        var body = image.Slice(at,46);
        var dialog = body.IndexOf((ReadOnlySpan<byte>)[0x52,0x68]);
        var flags = body.IndexOf((ReadOnlySpan<byte>)[0x81,0x4F,0x14]);
        if (dialog < 0 || flags < dialog + 6)
            throw new InvalidDataException("Unsupported original NPC service operands.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(body[11..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[(dialog+2)..]),BinaryPrimitives.ReadUInt32LittleEndian(body[(flags+3)..]))
        {
            TraderFlags = ReadPreviousFlag(image, at, 0x40, [0xA8,4,0x74,0x1A,0x8B,0xB7,0,2,0,0,0x8B,0x57,0x14,0x81,0xCE], 15),
            ComboMasterFlags = ReadPreviousFlag(image, at, 0x22, [0xA8,8,0x74,0x1A,0x8B,0xB7,0,2,0,0,0x8B,0x57,0x14,0x81,0xCE], 15),
            SmithFlags = ReadPreviousFlag(image, at, 0x6A, [0xA8,0x10,0x74,0x21,0x8B,0xB7,0,2,0,0,0x8B,0x57,0x14,0xB9,0,0,8,0,0x81,0xCE], 20)
        };
    }
    private static uint ReadPreviousFlag(ReadOnlySpan<byte> image, int anchor, int distance, ReadOnlySpan<byte> prefix, int operand)
    {
        if (anchor < distance) return 0; // Older bounded HorseMerchant-only evidence has no preceding block.
        var body = image[(anchor - distance)..];
        if (!body.StartsWith(prefix)) throw new InvalidDataException("Unsupported original merchant service operands.");
        return BinaryPrimitives.ReadUInt32LittleEndian(body[operand..]);
    }
}
