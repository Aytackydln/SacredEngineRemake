using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Reflection.PortableExecutable;
using System.Text;
using Sacred.Core.Executable;

namespace Sacred.Assets.Executable;

/// <summary>Original conversation exclusion predicate and symbolic type names.
/// Resource IDs and table bounds are extracted from native code, never substituted.</summary>
public sealed class SacredNpcConversationDefinitions
{
    private readonly FrozenSet<uint> _excluded;
    private readonly FrozenDictionary<uint, string> _names;
    private readonly uint _limit;
    private SacredNpcConversationDefinitions(HashSet<uint> excluded, Dictionary<uint, string> names, uint limit)
    { _excluded = excluded.ToFrozenSet(); _names = names.ToFrozenDictionary(); _limit = limit; }
    public bool Excluded(uint type) => type < _limit && _excluded.Contains(type);
    public string? TypeName(uint type) => _names.GetValueOrDefault(type);
    public bool ReactionType(uint type) => TypeName(type)?.Contains("_FEM", StringComparison.Ordinal) == true;

    public static SacredNpcConversationDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var filename in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, filename);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original NPC conversation definitions unavailable.", failure);
    }

    public static SacredNpcConversationDefinitions Read(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing executable header.");
        HashSet<uint>? excluded = null; uint limit = 0;
        Dictionary<uint, string>? names = null;
        ReadOnlySpan<byte> predicatePrefix = [0x8B, 0x44, 0x24, 4, 0x3D];
        ReadOnlySpan<byte> predicateEnd = [0xB0, 1, 0xC2, 4, 0, 0x32, 0xC0, 0xC2, 4, 0];
        // Follow the conversation handler's call after clearing its pending target/follow fields.
        // Several unrelated native predicates have the same sequence of type comparisons.
        ReadOnlySpan<byte> conversationCall = [0xC7, 0x86, 0x51, 2, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF,
            0x89, 0xBE, 0xF4, 1, 0, 0, 0xE8];
        ReadOnlySpan<byte> tablePrefix = [0x8D, 0x44, 0x24, 4, 0x56, 0x50, 0xB9];
        ReadOnlySpan<byte> tableEnd = [0x8D, 0x4E, 4, 0x83, 0xC6, 0x44, 0x81, 0xFE];
        for (var offset = 0; offset < image.Length - 128; offset++)
        {
            var code = image.AsSpan(offset);
            if (code[..conversationCall.Length].SequenceEqual(conversationCall))
            {
                var call = offset + conversationCall.Length - 1;
                var target = checked((ulong)((long)Address(call) + 5 + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(call + 1))));
                var predicate = At(target, 80);
                var end = predicate.IndexOf(predicateEnd);
                if (predicate[..5].SequenceEqual(predicatePrefix) && end == 60 && predicate[9] == 0x7D && predicate[10] == 0x36)
                {
                    var values = new HashSet<uint>(); var valid = true;
                    for (var index = 11; index < end; index += 7)
                    {
                        if (predicate[index] != 0x3D || predicate[index + 5] is not (0x74 or 0x75)) { valid = false; break; }
                        values.Add(BinaryPrimitives.ReadUInt32LittleEndian(predicate[(index + 1)..]));
                    }
                    if (valid) { excluded = values; limit = BinaryPrimitives.ReadUInt32LittleEndian(predicate[5..]); }
                }
            }
            if (code[0] != 0xBE || !code.Slice(5, 7).SequenceEqual(tablePrefix)) continue;
            var tail = code[..128].IndexOf(tableEnd);
            if (tail < 0) continue;
            var start = BinaryPrimitives.ReadUInt32LittleEndian(code[1..]);
            var endAddress = BinaryPrimitives.ReadUInt32LittleEndian(code[(tail + 8)..]);
            if (endAddress <= start || (endAddress - start) % SacredExecutableTypeNameLayout.SerializedSize != 0) continue;
            var data = At(start, checked((int)(endAddress - start)));
            names = new();
            for (var index = 0; index < data.Length; index += SacredExecutableTypeNameLayout.SerializedSize)
            {
                var row = data.Slice(index, SacredExecutableTypeNameLayout.SerializedSize);
                var text = row[4..]; var zero = text.IndexOf((byte)0);
                if (zero < 0) throw new InvalidDataException("Unterminated original type name.");
                names[BinaryPrimitives.ReadUInt32LittleEndian(row)] = Encoding.Latin1.GetString(text[..zero]);
            }
        }
        if (excluded is null || names is null || names.Count == 0) throw new InvalidDataException("Original conversation predicate/type-name table not found.");
        return new(excluded, names, limit);

        ulong Address(int offset)
        {
            foreach (var section in headers.SectionHeaders)
                if (offset >= section.PointerToRawData && offset < section.PointerToRawData + section.SizeOfRawData)
                    return imageBase + (ulong)(section.VirtualAddress + offset - section.PointerToRawData);
            throw new InvalidDataException("NPC conversation call is outside executable sections.");
        }

        ReadOnlySpan<byte> At(ulong address, int length)
        {
            if (address < imageBase) throw new InvalidDataException("NPC definition address precedes executable.");
            var rva = address - imageBase;
            foreach (var section in headers.SectionHeaders)
                if (rva >= (ulong)section.VirtualAddress && rva + (ulong)length <= (ulong)section.VirtualAddress + (ulong)section.SizeOfRawData)
                    return image.AsSpan(checked(section.PointerToRawData + (int)(rva - (ulong)section.VirtualAddress)), length);
            throw new InvalidDataException("NPC definition address is outside executable data.");
        }
    }
}
