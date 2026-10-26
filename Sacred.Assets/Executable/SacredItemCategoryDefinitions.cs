using System.Buffers.Binary;
using Sacred.Core.Pak.Items;

namespace Sacred.Assets.Executable;

/// <summary>Gold category weights and caller slot arrays. Reads original data
/// and checked instruction operands; resource numbers and weights are not invented.</summary>
public static class SacredItemCategoryDefinitions
{
    public static SacredItemCategoryRules Read(byte[] image)
    {
        var native = new SacredNativeTypeSwitch(image);
        var start = Find(image, [0x8B, 0x44, 0x24, 4, 0x53, 0x55, 0x48, 0x56, 0x57, 0x83, 0xF8, 8, 0x8B, 0xD9, 0xBF]);
        var body = image.AsSpan(start, 0x10C);
        Require(body, 0x13, [0x77]); Require(body, 0x15, [0xFF, 0x24, 0x85]);
        Require(body, 0x54, [0xB8]); Require(body, 0x6D, [0x3D]); Require(body, 0x8B, [0xD8, 0x0D]);
        var masks = new uint[9];
        var jumps = native.At(Word(body, 0x18), 36);
        for (var i = 0; i < masks.Length; i++)
        {
            var code = native.At(Word(jumps, i * 4), 5);
            Require(code, 0, [0xBF]); masks[i] = Word(code, 1);
        }
        var first = Word(body, 0x55);
        var end = Word(body, 0x6E);
        if (end <= first || (end - first) % 8 != 0 || end - first > 1024)
            throw new InvalidDataException("Invalid original category weight table.");
        var weights = new List<SacredItemCategoryWeight>();
        for (var address = first; address < end; address += 8)
        {
            var row = native.At(address - 4, 8);
            weights.Add(new(Category(Word(row, 0)), BinaryPrimitives.ReadUInt16LittleEndian(row[4..])));
        }
        var scale = BinaryPrimitives.ReadSingleLittleEndian(native.At(Word(body, 0x8D), 4));
        if (!float.IsFinite(scale) || scale <= 0 || scale >= 1) throw new InvalidDataException("Invalid category RNG scale.");

        var reward = Find(image, [0xB8, 0, 0, 0, 0, 0xC7, 0x84, 0x24, 0xD4, 0, 0, 0, 0, 0, 0, 0,
            0x89, 0x84, 0x24, 0xC0, 0, 0, 0], [1, 12]);
        var rewardRegister = Find(image, [0x8B, 0xE9, 0xBF, 0, 0, 0, 0, 0x89, 0x6C, 0x24, 0x18, 0x8B, 0xF7, 0x33, 0xDB], [3]);
        var rewardSlots = StackSlots(image.AsSpan(reward), 19, Word(image, rewardRegister + 3));
        var armor = Find(image, [0xC7, 0x84, 0x24, 0x7C, 1, 0, 0, 0, 0, 0, 0, 0xF7, 0xF9,
            0xC7, 0x84, 0x24, 0x80, 1, 0, 0], [7]);
        var armorSlots = StackSlots(image.AsSpan(armor), 11, 0);
        var jewelry = Find(image, [0xB8, 0, 0, 0, 0, 0x89, 0x84, 0x24, 0x70, 1, 0, 0,
            0xC7, 0x84, 0x24, 0x74, 1, 0, 0, 0, 0, 0, 0, 0x89, 0x84, 0x24, 0x78, 1, 0, 0], [1, 19]);
        var jewelrySlots = StackSlots(image.AsSpan(jewelry), 3, 0);
        // The armor branch compares ESI with this immediate before one redraw.
        var retry = Find(image, [0x8B, 0xB4, 0x94, 0x7C, 1, 0, 0, 0x83, 0xFE, 0, 0x75, 0x14, 0xE8], [], [9]);
        return new(weights.AsReadOnly(), Array.AsReadOnly(masks), Word(body, 0xF), scale,
            rewardSlots, armorSlots, Category(image[retry + 9]), jewelrySlots);
    }

    private static IReadOnlyList<SacredItemCategory> StackSlots(ReadOnlySpan<byte> body, int count, uint edi)
    {
        uint eax = 0;
        var values = new SortedDictionary<uint, SacredItemCategory>();
        for (var at = 0; values.Count < count && at < 300;)
        {
            var code = body[at..];
            if (code[0] == 0xB8) { eax = Word(code, 1); at += 5; }
            else if (code.StartsWith<byte>([0xC7, 0x84, 0x24])) { values.Add(Word(code, 3), Category(Word(code, 7))); at += 11; }
            else if (code.StartsWith<byte>([0x89, 0x84, 0x24])) { values.Add(Word(code, 3), Category(eax)); at += 7; }
            else if (code.StartsWith<byte>([0x89, 0xBC, 0x24])) { values.Add(Word(code, 3), Category(edi)); at += 7; }
            else if (code.StartsWith<byte>([0xF7, 0xF9])) at += 2;
            else throw new InvalidDataException("Unsupported original category slot initialization.");
        }
        if (values.Count != count || values.Keys.Last() - values.Keys.First() != (count - 1) * 4)
            throw new InvalidDataException("Invalid original category slots.");
        return Array.AsReadOnly(values.Values.ToArray());
    }

    private static SacredItemCategory Category(uint value) => value <= byte.MaxValue ? (SacredItemCategory)value :
        throw new InvalidDataException("Invalid original category number.");
    private static uint Word(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
    private static void Require(ReadOnlySpan<byte> bytes, int at, ReadOnlySpan<byte> expected)
    {
        if (!bytes[at..].StartsWith(expected)) throw new InvalidDataException("Unsupported original category instructions.");
    }
    private static int Find(ReadOnlySpan<byte> image, ReadOnlySpan<byte> pattern, ReadOnlySpan<int> words = default, ReadOnlySpan<int> bytes = default)
    {
        var found = -1;
        for (var at = 0; at <= image.Length - pattern.Length; at++)
        {
            if (image[at] != pattern[0]) continue;
            var matches = true;
            for (var i = 0; i < pattern.Length; i++)
            {
                var operand = bytes.Contains(i);
                foreach (var word in words) if (i >= word && i < word + 4) operand = true;
                if (!operand && image[at + i] != pattern[i]) { matches = false; break; }
            }
            if (!matches) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original category instructions.");
            found = at;
        }
        return found >= 0 ? found : throw new InvalidDataException("Original category instructions unavailable.");
    }
}
