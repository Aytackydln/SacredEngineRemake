using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

public static class SacredPotionLootDefinitions
{
    public static IReadOnlyList<uint> Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x8A, 0x54, 0x24, 0x0C, 0x57, 0xF6, 0xDA, 0x1B, 0xD2, 0xBE, 4, 0, 0, 0, 0x83, 0xE2, 2];
        var at = image.AsSpan().IndexOf(signature);
        if (at < 0 || image.AsSpan(at + 1).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original potion selection function unavailable or ambiguous.");
        var start = at - 0x1B;
        if (start < 0 || start > image.Length - 0xEF) throw new InvalidDataException("Truncated potion instructions.");
        var body = image.AsSpan(start, 0xEF);
        if (!body.Slice(0x78, 3).SequenceEqual<byte>([0x8B, 4, 0x85]))
            throw new InvalidDataException("Unsupported original potion table instruction.");
        var address = BinaryPrimitives.ReadUInt32LittleEndian(body[0x7B..]);
        var native = new SacredNativeTypeSwitch(image);
        var bytes = native.At(address, 18 * sizeof(uint));
        var table = new uint[18];
        for (var i = 0; i < table.Length; i++) table[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
        return Array.AsReadOnly(table);
    }
}
