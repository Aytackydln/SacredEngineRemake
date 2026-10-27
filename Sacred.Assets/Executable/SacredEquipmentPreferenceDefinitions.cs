using System.Buffers.Binary;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Executable;

/// <summary>Checked final-selection resource aliases; no production resource
/// constants or assumptions about the aliases' gameplay names.</summary>
public static class SacredEquipmentPreferenceDefinitions
{
    public static SacredEquipmentPreferenceRules Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x7F, 0x37, 0x74, 0x55, 0x8D, 0x4F, 0xFF, 0x83, 0xF9, 8, 0x0F, 0x87];
        var found = image.AsSpan().IndexOf(signature);
        var start = found - 3;
        if (found < 3 || start > image.Length - 0xA0 || image.AsSpan(found + 1).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original final equipment preferences unavailable.");
        var code = image.AsSpan(start, 0xA0);
        Require(code, 0, [0x83, 0xFF]); Require(code, 0x3C, [0x81, 0xFF]); Require(code, 0x44, [0x81, 0xFF]);
        Require(code, 0x4E, [0x81, 0xE9]); Require(code, 0x56, [0x49, 0x74]); Require(code, 0x59, [0x49, 0x75]);
        Require(code, 0x5C, [0xA8]); Require(code, 0x70, [0xA8]);
        Require(code, 0x84, [0x81, 0xFF]); Require(code, 0x8C, [0x81, 0xFF]); Require(code, 0x94, [0x81, 0xFF]); Require(code, 0x9C, [0xA8]);
        var masks = new Dictionary<uint, uint> { [code[2]] = code[0x5D] };
        var baseResource = Word(code, 0x50);
        masks.Add(baseResource, code[0x71]); masks.Add(baseResource + 1, code[0x9D]); masks.Add(baseResource + 2, code[0x5D]);
        AddRange(Word(code, 0x46), Word(code, 0x3E));
        AddRange(Word(code, 0x86), Word(code, 0x8E));
        masks.Add(Word(code, 0x96), code[0x9D]);
        return new(masks);

        void AddRange(uint first, uint last)
        {
            if (last < first || last - first > 64) throw new InvalidDataException("Invalid native preference range.");
            for (var resource = first; resource <= last; resource++) masks.Add(resource, image[start + 0x9D]);
        }
    }

    private static uint Word(ReadOnlySpan<byte> code, int at) => BinaryPrimitives.ReadUInt32LittleEndian(code[at..]);
    private static void Require(ReadOnlySpan<byte> code, int at, ReadOnlySpan<byte> expected)
    {
        if (!code[at..].StartsWith(expected)) throw new InvalidDataException("Unsupported native equipment preference instructions.");
    }
}
