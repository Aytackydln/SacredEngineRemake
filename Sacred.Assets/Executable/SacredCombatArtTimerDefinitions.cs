using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.CombatArts;

namespace Sacred.Assets.Executable;

/// <summary>Original definition tables found through 4D1AF0/4D2310 lookup
/// instructions. Row zero is deliberately excluded, matching the native scans.</summary>
public sealed class SacredCombatArtTimerDefinitions
{
    private readonly Dictionary<ushort, SacredSpellDefinitionLayout> _spells;
    private readonly Dictionary<ushort, SacredMagicDefinitionLayout> _magic;
    private SacredCombatArtTimerDefinitions(Dictionary<ushort, SacredSpellDefinitionLayout> spells,
        Dictionary<ushort, SacredMagicDefinitionLayout> magic) => (_spells, _magic) = (spells, magic);

    public bool TryGetSpell(ushort code, out SacredSpellDefinitionLayout value) => _spells.TryGetValue(code, out value);
    public bool TryGetMagic(ushort code, out SacredMagicDefinitionLayout value) => _magic.TryGetValue(code, out value);

    public static SacredCombatArtTimerDefinitions Read(byte[] image)
    {
        var native = new SacredNativeTypeSwitch(image);
        var spells = Table(SacredSpellDefinitionLayout.Size);
        var magic = Table(SacredMagicDefinitionLayout.Size);
        return new(spells.Select(row => MemoryMarshal.Read<SacredSpellDefinitionLayout>(row)).GroupBy(row => row.Code).ToDictionary(group => group.Key, group => group.First()),
            magic.Select(row => MemoryMarshal.Read<SacredMagicDefinitionLayout>(row)).GroupBy(row => row.Code).ToDictionary(group => group.Key, group => group.First()));

        List<byte[]> Table(int stride)
        {
            ReadOnlySpan<byte> prefix = [0x66,0x8B,0x54,0x24,4,0xB9,1,0,0,0,0xB8];
            var found = -1;
            uint first = 0, end = 0;
            for (var at = 0; at < image.Length - 40; at++)
            {
                var code = image.AsSpan(at);
                if (!code.StartsWith(prefix) || !code.Slice(15, 4).SequenceEqual<byte>([0x66,0x39,0x10,0x74])) continue;
                var increment = stride == 0x78 ? code.Slice(20, 3).SequenceEqual<byte>([0x83,0xC0,0x78]) : code.Slice(20, 5).SequenceEqual<byte>([0x05,0x94,0,0,0]);
                var compare = stride == 0x78 ? 24 : 26;
                if (!increment || code[compare - 1] != 0x41 || code[compare] != 0x3D || code[compare + 5] != 0x72) continue;
                if (found >= 0) throw new InvalidDataException("Ambiguous original art definition lookup.");
                found = at; first = BinaryPrimitives.ReadUInt32LittleEndian(code[11..]); end = BinaryPrimitives.ReadUInt32LittleEndian(code[(compare + 1)..]);
            }
            if (found < 0 || end <= first || (end - first) % stride != 0 || (end - first) / stride > 1024)
                throw new InvalidDataException("Original art regeneration table lookup unavailable.");
            var rows = new List<byte[]>();
            for (var at = first; at < end; at += (uint)stride) rows.Add(native.At(at, stride).ToArray());
            return rows;
        }
    }
}
