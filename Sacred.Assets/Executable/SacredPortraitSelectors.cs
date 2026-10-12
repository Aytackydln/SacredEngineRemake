using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.UI;

namespace Sacred.Assets.Executable;

/// <summary>Reads TypeManager's original portrait switch; no actor IDs or portrait mappings are synthesized.</summary>
public static class SacredPortraitSelectors
{
    public static IReadOnlyDictionary<uint, int> Read(byte[] image,
        IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> definitions)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing executable header.");
        ReadOnlySpan<byte> signature = [0x8B, 0x4C, 0x24, 0x04, 0x33, 0xC0, 0x49, 0x81, 0xF9];
        Dictionary<uint, int>? result = null;
        for (var offset = 0; offset <= image.Length - 34; offset++)
        {
            if (!image.AsSpan(offset, signature.Length).SequenceEqual(signature)) continue;
            var code = image.AsSpan(offset, 34);
            if (code[13] != 0x0F || code[14] != 0x87) continue;
            var limit = BinaryPrimitives.ReadUInt32LittleEndian(code[9..]);
            if (limit > 65535) continue;
            var direct = code.Slice(19, 3).SequenceEqual(new byte[] { 0xFF, 0x24, 0x8D });
            var compressed = code.Slice(19, 4).SequenceEqual(new byte[] { 0x33, 0xD2, 0x8A, 0x91 }) &&
                code.Slice(27, 3).SequenceEqual(new byte[] { 0xFF, 0x24, 0x95 });
            if (!direct && !compressed) continue;
            var indices = compressed ? BinaryPrimitives.ReadUInt32LittleEndian(code[23..]) : 0;
            var targets = BinaryPrimitives.ReadUInt32LittleEndian(code[(direct ? 22 : 30)..]);
            var mapping = new Dictionary<uint, int>();
            var valid = true;
            try
            {
                for (uint index = 0; index <= limit; index++)
                {
                    var caseIndex = compressed ? At(indices + index, 1)[0] : index;
                    var target = BinaryPrimitives.ReadUInt32LittleEndian(At(targets + caseIndex * 4, 4));
                    var body = At(target, 8);
                    // EAX starts at zero; the default case returns it unchanged.
                    if (body[0] == 0xC2 && body[1] == 4 && body[2] == 0) continue;
                    if (body[0] != 0xB8 || body[5] != 0xC2 || body[6] != 4 || body[7] != 0)
                    { valid = false; break; }
                    var selector = BinaryPrimitives.ReadInt32LittleEndian(body[1..]);
                    if (selector <= 0 || selector >= definitions.Count ||
                        !SacredUiTextureTable.GetTextureName(definitions[selector]).Contains("portrait", StringComparison.OrdinalIgnoreCase))
                    { valid = false; break; }
                    mapping.Add(index + 1, selector);
                }
            }
            catch (InvalidDataException) { valid = false; }
            if (!valid || mapping.Count < 8) continue;
            if (result is not null) throw new InvalidDataException("Ambiguous native portrait switches.");
            result = mapping;
        }
        return result ?? throw new InvalidDataException("No native portrait selector switch found in this executable.");

        ReadOnlySpan<byte> At(ulong address, int length)
        {
            if (address < imageBase) throw new InvalidDataException("Portrait switch address precedes the image.");
            var rva = address - imageBase;
            foreach (var section in headers.SectionHeaders)
            {
                if (rva < (ulong)section.VirtualAddress || rva + (ulong)length > (ulong)section.VirtualAddress + (ulong)section.SizeOfRawData) continue;
                var offset = checked(section.PointerToRawData + (int)(rva - (ulong)section.VirtualAddress));
                if (offset < 0 || offset > image.Length - length) break;
                return image.AsSpan(offset, length);
            }
            throw new InvalidDataException("Portrait switch address is outside executable data.");
        }
    }
}
