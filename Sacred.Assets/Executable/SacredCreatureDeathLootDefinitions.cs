using Sacred.Core.Loot;

namespace Sacred.Assets.Executable;

/// <summary>Checked resource exceptions in Gold's death loot gate.</summary>
public static class SacredCreatureDeathLootDefinitions
{
    public static SacredCreatureDeathLootRules Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x8B, 0x8D, 0xB0, 0, 0, 0, 0x83, 0xC9, 2, 0x85, 0xDB, 0x89, 0x8D, 0xB0, 0, 0, 0];
        var start = image.AsSpan().IndexOf(signature);
        if (start < 0 || image.AsSpan(start + 1).IndexOf(signature) >= 0 || start > image.Length - 0xA9)
            throw new InvalidDataException("Original death loot gate unavailable or ambiguous.");
        Require(0x1D, [0x83, 0x7B, 0x10]);
        Require(0x97, [0x83, 0xF8]); Require(0xA0, [0x83, 0xF8]);
        return new(image[start + 0x20], Array.AsReadOnly(new uint[] { image[start + 0x99], image[start + 0xA2] }));

        void Require(int offset, ReadOnlySpan<byte> bytes)
        {
            if (!image.AsSpan(start + offset).StartsWith(bytes)) throw new InvalidDataException("Unsupported death loot resource predicate.");
        }
    }
}
