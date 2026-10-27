using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;

namespace Sacred.Assets.Executable;

/// <summary>Gold boosted-source jump tables and checked reward operands.
/// No creature or reward item IDs are maintained as remake constants.</summary>
public static class SacredCreatureDeathRewardDefinitions
{
    public static SacredCreatureDeathRewardRules Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x3D, 0x90, 0, 0, 0, 0x0F, 0x8F];
        var start = image.AsSpan().IndexOf(signature);
        if (start < 0 || image.AsSpan(start + 1).IndexOf(signature) >= 0 || start > image.Length - 0x784)
            throw new InvalidDataException("Original death reward dispatch unavailable or ambiguous.");
        var native = new SacredNativeTypeSwitch(image);
        var address = native.Address(start);
        Require(17, [0x83, 0xC0]); Require(20, [0x83, 0xF8]); Require(31, [0x8A, 0x90]); Require(37, [0xFF, 0x24, 0x95]);
        // The first recipe reads Balance.bin +0x1224. Its global address moves
        // between Gold images; later bonus operands index this same table.
        Require(0x2C, [0x8B, 0x1D]);
        var rewardBonusesAddress = Word(0x2E);
        var normalEnd = Target(23);
        var resources = new Dictionary<uint, SacredCreatureDeathRewardRule>();
        ReadTable(unchecked((uint)-(sbyte)image[start + 19]), image[start + 22] + 1, Word(33), Word(40));
        Add(Word(1), Target(11));
        var high = native.At(Target(5), 33);
        if (!high[..2].SequenceEqual<byte>([0x8D, 0xB8]) || !high.Slice(20, 2).SequenceEqual<byte>([0x8A, 0x87]) ||
            !high.Slice(26, 3).SequenceEqual<byte>([0xFF, 0x24, 0x85])) throw new InvalidDataException("Unsupported upper reward dispatch.");
        ReadTable(unchecked((uint)-BinaryPrimitives.ReadInt32LittleEndian(high[2..])),
            checked((int)BinaryPrimitives.ReadUInt32LittleEndian(high[8..]) + 1),
            BinaryPrimitives.ReadUInt32LittleEndian(high[22..]), BinaryPrimitives.ReadUInt32LittleEndian(high[29..]));

        Require(0x2FB, [0x83, 0xF8]); Require(0x304, [0x83, 0xF8]); Require(0x33B, [0xFF, 0x24, 0x85]);
        var heroTable = native.At(Word(0x33E), 9 * 4);
        var heroResources = new uint[9];
        for (var i = 0; i < heroResources.Length; i++)
        {
            var code = native.At(BinaryPrimitives.ReadUInt32LittleEndian(heroTable[(i * 4)..]), 11);
            if (!code[..7].SequenceEqual<byte>([0x6A, 0, 0x6A, 0, 0x6A, 0, 0x68])) throw new InvalidDataException("Unsupported hero set reward.");
            heroResources[i] = BinaryPrimitives.ReadUInt32LittleEndian(code[7..]);
        }
        Require(0x4B9, [0xB8]); Require(0x4F9, [0xB8]); Require(0x519, [0xB8]);
        var categories = Enumerable.Repeat((SacredItemCategory)Word(0x4BA), 7).ToList();
        for (var i = 0; i < 10; i++)
        {
            var at = 0x522 + i * 11;
            Require(at, [0xC7, 0x84, 0x24]); categories.Add((SacredItemCategory)Word(at + 7));
        }
        categories.AddRange(Enumerable.Repeat((SacredItemCategory)Word(0x4FA), 3));
        categories.AddRange(Enumerable.Repeat((SacredItemCategory)Word(0x51A), 2));
        Require(0x659, [0x81, 0xF9]); Require(0x661, [0x81, 0xF9]);
        return new(resources, image[start + 0x2FD], image[start + 0x306], Array.AsReadOnly(heroResources),
            categories.AsReadOnly(), Word(0x65B), Word(0x663));

        void ReadTable(uint first, int count, uint byteAddress, uint jumpAddress)
        {
            if (count is <= 0 or > 4096) throw new InvalidDataException("Invalid original death reward table.");
            var indices = native.At(byteAddress, count).ToArray();
            foreach (var (index, offset) in indices.Select((value, i) => (value, i)))
                Add(first + (uint)offset, BinaryPrimitives.ReadUInt32LittleEndian(native.At(jumpAddress + index * 4U, 4)));
        }
        void Add(uint resource, ulong target)
        {
            if (target == normalEnd) return;
            var code = native.At(target, 53);
            SacredCreatureDeathRewardRule rule;
            if (code[..2].SequenceEqual<byte>([0x8B, 0x1D]))
            {
                if (code[6] != 0x6A || code[8] != 0x6A || code[28] != 0x6A || code[30] != 0x6A || code[32] != 0x6A || code[34] != 0x68)
                    throw new InvalidDataException("Unsupported equipment reward recipe.");
                rule = new(SacredCreatureDeathRewardKind.Equipment, Bonus(BinaryPrimitives.ReadUInt32LittleEndian(code[2..])),
                    code[9], code[7], new(BinaryPrimitives.ReadUInt32LittleEndian(code[35..]), code[33], code[31], code[29]));
            }
            else if (code[..2].SequenceEqual<byte>([0x03, 0x35]))
            {
                if (code[6] != 0x6A || code[0x29] != 0xBB) throw new InvalidDataException("Unsupported unique world reward.");
                rule = new(SacredCreatureDeathRewardKind.UniqueWorldResource, Bonus(BinaryPrimitives.ReadUInt32LittleEndian(code[2..])),
                    Resource: BinaryPrimitives.ReadUInt32LittleEndian(code[0x2A..]), RandomMaximum: code[7]);
            }
            else if (code[..2].SequenceEqual<byte>([0x33, 0xFF])) rule = new(SacredCreatureDeathRewardKind.Jackpot);
            else if (code[..2].SequenceEqual<byte>([0x8B, 0x15])) rule = new(SacredCreatureDeathRewardKind.ResetBoost);
            else if (code[0] == 0x6A && code[2] == 0x6A && code[18] == 0xA1)
                rule = new(SacredCreatureDeathRewardKind.ExtraCalls, Bonus(BinaryPrimitives.ReadUInt32LittleEndian(code[19..])), code[3], code[1]);
            else if (code[0] == 0x6A && code[2] == 0x6A && code[0x23] == 0xBB)
                rule = new(SacredCreatureDeathRewardKind.UniqueWorldResource,
                    Resource: BinaryPrimitives.ReadUInt32LittleEndian(code[0x24..]), RandomMaximum: code[1]);
            else if (code[0] == 0xA1 && code.Slice(5, 8).SequenceEqual<byte>([0xC7, 0x44, 0x24, 0x10, 0, 0, 0, 0]))
                rule = new(SacredCreatureDeathRewardKind.SetResource);
            else if (code[0] == 0xA1) rule = new(SacredCreatureDeathRewardKind.Bonus, Bonus(BinaryPrimitives.ReadUInt32LittleEndian(code[1..])));
            else throw new InvalidDataException($"Unsupported original death reward at 0x{target:X}.");
            resources.Add(resource, rule);
        }
        int Bonus(uint globalAddress)
        {
            var offset = globalAddress - rewardBonusesAddress;
            if (offset % 4 != 0 || offset / 4 >= SacredLootBalanceLayout.CreatureDeathRewardBonusCount) throw new InvalidDataException("Invalid original death bonus operand.");
            return (int)(offset / 4);
        }
        ulong Target(int at) => unchecked((ulong)((long)address + at + 6 + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(start + at + 2))));
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(start + at));
        void Require(int at, ReadOnlySpan<byte> bytes)
        {
            if (!image.AsSpan(start + at).StartsWith(bytes)) throw new InvalidDataException($"Unsupported death reward operand at +0x{at:X}.");
        }
    }
}
