using System.Buffers.Binary;
using System.Text;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Executable;

/// <summary>Original Gold 0x440560 resource/name rules. The recognized instruction
/// shape is checked before extracting resource numbers, strings and class bits.</summary>
public static class SacredEquipmentClassDefinitions
{
    public static SacredEquipmentClassRules Read(byte[] image)
    {
        ReadOnlySpan<byte> prefix = [0x53, 0x55, 0x56, 0x57, 0x8B, 0x7C, 0x24, 0x14, 0x85, 0xFF, 0x0F, 0x84];
        var offset = -1;
        for (var search = 0; search < image.Length - 0x6CA;)
        {
            var relative = image.AsSpan(search).IndexOf(prefix);
            if (relative < 0) break;
            var candidate = search + relative;
            search = candidate + prefix.Length;
            if (candidate > image.Length - 0x6CA) continue;
            var gates = true;
            for (var i = 0; i < 7; i++)
            {
                var at = candidate + 0x12 + i * 18;
                if (image[at] != 0x3D || image[at + 5] != 0x7E || image[at + 7] != 0x3D || image[at + 12] != 0x0F || image[at + 13] != 0x8C)
                { gates = false; break; }
            }
            if (!gates) continue;
            if (offset >= 0) throw new InvalidDataException("Ambiguous original equipment class function.");
            offset = candidate;
        }
        if (offset < 0) throw new InvalidDataException("Original equipment class function unavailable.");
        var native = new SacredNativeTypeSwitch(image);
        return ReadCode(image.AsSpan(offset, 0x6CA).ToArray(), checked((uint)native.Address(offset)), address =>
        {
            var text = native.At(address, 64);
            var end = text.IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("Unterminated original class-name pattern.");
            return Encoding.Latin1.GetString(text[..end]);
        });
    }

    public static SacredEquipmentClassRules ReadCode(byte[] body, uint address, Func<uint, string> readName)
    {
        ArgumentNullException.ThrowIfNull(readName);
        if (body.Length < 0x6CA) throw new InvalidDataException("Truncated original equipment class function.");
        var excluded = new SacredEquipmentResourceRange[7];
        for (var i = 0; i < excluded.Length; i++)
        {
            var at = 0x12 + i * 18;
            Require(at, [0x3D]); Require(at + 5, [0x7E]); Require(at + 7, [0x3D]); Require(at + 12, [0x0F, 0x8C]);
            excluded[i] = new(checked(Word(at + 1) + 1), checked(Word(at + 8) - 1));
        }
        Require(0x9E, [0x3D]); Require(0xA5, [0x3D]); Require(0xAC, [0x3D]); Require(0xB3, [0x3D]);
        var parents = new[] { new SacredEquipmentResourceRange(Word(0x9F), Word(0xA6)), new SacredEquipmentResourceRange(Word(0xAD), Word(0xB4)) };
        Require(0x122, [0x83, 0xF9]);
        var links = body[0x124] + 1;
        var preserved = new HashSet<uint>();
        for (var i = 0; i < 6; i++) { Require(0x12F + i * 7, [0x3D]); preserved.Add(Word(0x130 + i * 7)); }
        Require(0x159, [0xF6, 0x47, 4, 0x40]);
        Require(0x1C8, [0x25]); Require(0x578, [0x25]);
        var mask = Word(0x1C9);
        if (mask != Word(0x579)) throw new InvalidDataException("Inconsistent original class clear mask.");
        Require(0x542, [0x32, 0xC0]); Require(0x6C4, [0xB0, 1]);
        var target = CallTarget(0x184);
        string Name(int at)
        {
            Require(at, [0x68]);
            var name = readName(Word(at + 1));
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("Empty original class-name pattern.");
            return name;
        }
        SacredEquipmentClassNameRule[] Rules(int start, int end)
        {
            var result = new List<SacredEquipmentClassNameRule>();
            for (var at = start; at < end; at++)
            {
                if (body[at] != 0x68) continue;
                var call = at + 6;
                if (body[at + 5] == 0x25) { Require(at + 10, [0x56, 0x89, 0x47, 4]); call = at + 14; }
                else if (body[at + 5] != 0x56) continue;
                if (body[call] != 0xE8 || CallTarget(call) != target) continue;
                var flag = SacredEquipmentClassRuleTrace.MatchedFlag(body, call + 5);
                if ((flag & mask) != 0) throw new InvalidDataException("Original class rule writes an unrelated flag.");
                result.Add(new(Name(at), flag));
                at = call + 4;
            }
            if (result.Count == 0) throw new InvalidDataException("Missing original equipment class rules.");
            return result.ToArray();
        }
        return new(excluded, parents, links, preserved, mask, Name(0x17E), [Name(0x194), Name(0x1AA)],
            Rules(0x1C3, 0x53F), [Name(0x548), Name(0x55A)], Rules(0x573, 0x6BA));

        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(at));
        uint CallTarget(int at) { Require(at, [0xE8]); return unchecked(address + (uint)at + 5 + (uint)BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(at + 1))); }
        void Require(int at, ReadOnlySpan<byte> expected)
        {
            if (at < 0 || at > body.Length - expected.Length || !body.AsSpan(at, expected.Length).SequenceEqual(expected))
                throw new InvalidDataException("Unsupported original equipment class instructions.");
        }
    }

    public static SacredEquipmentClassRules Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment class definitions unavailable.", failure);
    }
}
