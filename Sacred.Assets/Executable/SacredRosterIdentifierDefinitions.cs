using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.Executable.Layouts;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Original formatting-removal operands used by the roster consumer.
/// Reads the installed executable's strings and lengths without copied rules.</summary>
public sealed class SacredRosterIdentifierDefinitions
{
    private SacredRosterIdentifierDefinitions(SacredTextMarkup markup) => Markup = markup;
    public SacredTextMarkup Markup { get; }

    public static SacredRosterIdentifierDefinitions Read(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing original PE header.");
        var signature = SacredExecutableRosterIdentifierLayout.Prologue;
        var found = image.AsSpan().IndexOf(signature);
        var start = found - SacredExecutableRosterIdentifierLayout.PrologueOffset;
        if (start < 0 || image.AsSpan(found + signature.Length).IndexOf(signature) >= 0 ||
            start > image.Length - SacredExecutableRosterIdentifierLayout.BodySize)
            throw new InvalidDataException("Original roster identifier function missing or ambiguous.");
        var body = image.AsSpan(start, SacredExecutableRosterIdentifierLayout.BodySize);
        if (!body[..3].SequenceEqual<byte>([0x6a, 0xff, 0x68]) || !body.Slice(7, 6).SequenceEqual<byte>([0x64, 0xa1, 0, 0, 0, 0]) ||
            !body[^3..].SequenceEqual<byte>([0xc2, 0x10, 0]))
            throw new InvalidDataException("Unknown original roster identifier function extent.");
        var prefixes = SacredExecutableRosterIdentifierLayout.PrefixOperandOffsets;
        var lengths = SacredExecutableRosterIdentifierLayout.RemovalLengthOperandOffsets;
        var rules = new SacredTextMarkupRemoval[prefixes.Length];
        for (var index = 0; index < rules.Length; index++)
        {
            var prefixAt = prefixes[index];
            var repeatedAt = prefixAt + SacredExecutableRosterIdentifierLayout.RepeatedPrefixOperandDistance;
            if (body[prefixAt - 1] != 0x68 || body[repeatedAt - 1] != 0x68 ||
                !body.Slice(prefixAt, 4).SequenceEqual(body.Slice(repeatedAt, 4)) ||
                body[lengths[index] - 1] is not (0xbb or 0xbf))
                throw new InvalidDataException("Unknown original roster formatting operands.");
            var address = BinaryPrimitives.ReadUInt32LittleEndian(body[prefixAt..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(body[lengths[index]..]);
            var prefix = ReadPrefix(address);
            if (prefix.Length == 0 || length <= 0)
                throw new InvalidDataException("Invalid original roster formatting rule.");
            rules[index] = new(prefix, length);
        }
        return new(new(rules));

        string ReadPrefix(uint address)
        {
            var characters = new List<char>();
            // Bounds follow the stored PE section; preserve raw UTF16 units.
            for (var position = (ulong)address; position <= uint.MaxValue - 1; position += 2)
            {
                var value = BinaryPrimitives.ReadUInt16LittleEndian(At((uint)position));
                if (value == 0) return new string(characters.ToArray());
                characters.Add((char)value);
            }
            throw new InvalidDataException("Unterminated original roster formatting prefix.");
        }

        ReadOnlySpan<byte> At(uint address)
        {
            if (address < imageBase) throw new InvalidDataException("Original formatting address precedes image.");
            var rva = (ulong)address - imageBase;
            foreach (var section in headers.SectionHeaders)
            {
                if (rva < (uint)section.VirtualAddress) continue;
                var relative = rva - (uint)section.VirtualAddress;
                if (relative + 2 > (uint)section.SizeOfRawData) continue;
                var offset = (ulong)(uint)section.PointerToRawData + relative;
                if (offset + 2 > (ulong)image.Length) break;
                return image.AsSpan(checked((int)offset), 2);
            }
            throw new InvalidDataException("Original formatting prefix lies outside stored sections.");
        }
    }

    public static SacredRosterIdentifierDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original roster identifier definitions unavailable.", failure);
    }
}
