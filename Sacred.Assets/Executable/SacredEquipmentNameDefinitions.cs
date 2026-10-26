using System.Buffers.Binary;
using System.Text;
using Sacred.Core.Pak.Weapon.Names;

namespace Sacred.Assets.Executable;

/// <summary>Checked Gold name-generation operands and resource fragments.
/// Original executable data supplies every family/default/prefix string.</summary>
public static class SacredEquipmentNameDefinitions
{
    public static SacredEquipmentNameRules Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x81, 0xEC, 0xE4, 3, 0, 0, 0x53, 0x55, 0x56, 0x8B, 0xF1,
            0x57, 0x89, 0x74, 0x24, 0x14, 0x8B, 0xAE, 0x18, 1, 0, 0];
        var start = image.AsSpan().IndexOf(signature);
        if (start < 0 || image.AsSpan(start + 1).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original item naming function unavailable or ambiguous.");
        start -= 0x15;
        if (start < 0 || start > image.Length - 0x1128) throw new InvalidDataException("Truncated item naming function.");
        var native = new SacredNativeTypeSwitch(image);
        var address = native.Address(start);
        var body = image.AsSpan(start, 0x1128);
        Require(body, 0x5B, [0x8B, 0x0D]);
        var prefix = Text(Word(body, 0x5D));
        var rarity = new[] { Immediate(0x192), Immediate(0x18B), Immediate(0x17D), Immediate(0x16D) };
        var elements = new[] { Immediate(0x203), Immediate(0x1F8), Immediate(0x1E3), Immediate(0x1CE) };
        Require(body, 0x28E, [0xFF, 0x24, 0xB5]);
        var familyTable = native.At(Word(body, 0x291), 33 * 4);
        var families = new string[33];
        for (var i = 0; i < families.Length; i++)
        {
            var target = Word(familyTable, i * 4);
            var code = native.At(target, 0x160);
            var operand = -1;
            for (var at = 0; at < code.Length - 8; at++)
            {
                if (code[at] != 0xBF) continue;
                if ((at >= 4 && code.Slice(at - 4, 4).SequenceEqual<byte>([0x8D, 0x54, 0x24, 0x34])) ||
                    code.Slice(at + 5, 3).SequenceEqual<byte>([0x83, 0xC9, 0xFF])) { operand = at + 1; break; }
            }
            if (operand < 0) throw new InvalidDataException("Original item name family unavailable.");
            if (i == 4) { Require(code, 0x28, [0xBF]); operand = 0x29; } // Two-handed branch; first literal is its one-handed override.
            families[i] = Text(Word(code, operand));
        }
        Require(body, 0x272, [0xFF, 0x24, 0x85]);
        var usageTable = native.At(Word(body, 0x275), 14 * 4);
        var usageTypes = new int[14];
        for (var i = 0; i < usageTypes.Length; i++)
        {
            var target = Word(usageTable, i * 4);
            var code = native.At(target, 10);
            usageTypes[i] = code[0] == 0xBE ? checked((int)Word(code, 1)) : target == address + 0x279 ? -2 : -1;
        }
        var defaultsCode = native.At(Call(0xF3), 0x24);
        Require(defaultsCode, 0x14, [0x8B, 4, 0x85]);
        var defaultsTable = native.At(Word(defaultsCode, 0x17), 33 * 4);
        var defaults = new string[33];
        for (var i = 0; i < defaults.Length; i++) defaults[i] = Text(Word(defaultsTable, i * 4));
        var swordCode = native.At(Call(0x27E), 0xA2).ToArray();
        string Pattern(int at) { Require(swordCode, at, [0x68]); return Text(Word(swordCode, at + 1)); }
        return new(prefix, rarity, elements, families, defaults, usageTypes,
            [Pattern(0x1C), Pattern(0x2E)], [Pattern(0x40), Pattern(0x52)]);

        string Immediate(int at) { var code = image.AsSpan(start, 0x1128); Require(code, at, [0xBF]); return Text(Word(code, at + 1)); }
        ulong Call(int at)
        {
            var code = image.AsSpan(start, 0x1128); Require(code, at, [0xE8]);
            return unchecked((ulong)((long)address + at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code[(at + 1)..])));
        }
        string Text(ulong at)
        {
            var data = native.At(at, 64);
            var end = data.IndexOf((byte)0);
            if (end < 1) throw new InvalidDataException("Invalid item name fragment.");
            return Encoding.Latin1.GetString(data[..end]);
        }
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
    private static void Require(ReadOnlySpan<byte> bytes, int at, ReadOnlySpan<byte> expected)
    {
        if (at < 0 || at > bytes.Length - expected.Length || !bytes[at..].StartsWith(expected))
            throw new InvalidDataException("Unsupported original item naming instructions.");
    }
}
