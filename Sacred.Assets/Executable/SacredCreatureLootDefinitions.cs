using System.Buffers.Binary;
using Sacred.Core.Loot;

namespace Sacred.Assets.Executable;

/// <summary>Checked original creature-drop instructions. Resource IDs come
/// from the executable; callers never invent replacements for special loot.</summary>
public static class SacredCreatureLootDefinitions
{
    public static SacredCreatureLootResources Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x8B, 0xBC, 0x24, 0xF8, 7, 0, 0, 0x33, 0xDB, 0x8B, 0xF1];
        var start = image.AsSpan().IndexOf(signature);
        if (start < 0 || image.AsSpan(start + 1).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original creature loot function unavailable or ambiguous.");
        start -= 0x1F;
        if (start < 0 || start > image.Length - 0xD80) throw new InvalidDataException("Truncated creature loot instructions.");
        var native = new SacredNativeTypeSwitch(image);
        Require(0x85D, [0xB9]);
        var runeCount = Word(0x85E);
        if (runeCount is 0 or > 4096) throw new InvalidDataException("Invalid original rune resource count.");
        var runes = new uint[runeCount];
        Require(0x86A, [0x8B, 0x3C, 0x95]);
        var table = native.At(Word(0x86D), runes.Length * sizeof(uint));
        for (var i = 0; i < runes.Length; i++) runes[i] = BinaryPrimitives.ReadUInt32LittleEndian(table[(i * 4)..]);
        var excluded = new uint[3];
        Require(0x880, [0x3D]); excluded[0] = Word(0x881);
        Require(0x887, [0x3D]); excluded[1] = Word(0x888);
        Require(0x88E, [0x3D]); excluded[2] = Word(0x88F);
        var money = new[] { Resource(0xBE0), Resource(0xBD9), Resource(0xBD2), Resource(0xBCB) };
        Require(0xD70, [0xC7, 0x44, 0x24, 0x1C]);
        Require(0xC14, [0x81, 0xFF]);
        return new(Array.AsReadOnly(money), Array.AsReadOnly(runes), Array.AsReadOnly(excluded), Word(0xD74))
        {
            PotionTable = SacredPotionLootDefinitions.Read(image),
            ProgressionForcedResources = Array.AsReadOnly(new[] { Resource(0x109), Resource(0x110), Resource(0x117), Resource(0x11E), Resource(0x125) }),
            MaximumAcceptedResource = checked((int)Word(0xC16))
        };

        uint Resource(int offset) { Require(offset, [0xBF]); return Word(offset + 1); }
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(start + offset));
        void Require(int offset, ReadOnlySpan<byte> expected)
        {
            if (!image.AsSpan(start + offset).StartsWith(expected))
                throw new InvalidDataException($"Unsupported original creature loot instruction at +0x{offset:X}.");
        }
    }
}
