using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

/// <summary>Read importer transient keys and masking text from the executable.
/// This reader neither loads Settings.cfg nor supplies implicit settings values.</summary>
public static class SacredSessionImportDefinitions
{
    public static SacredSessionImportDefinition Read(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing executable image base.");
        var signature = SacredExecutableSessionImportLayout.Signature;
        var found = image.AsSpan().IndexOf(signature);
        if (found < 0 || image.AsSpan(found + signature.Length).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original session importer missing or ambiguous.");
        var at = found - SacredExecutableSessionImportLayout.SignatureOffset;
        if (at < 0 || at > image.Length - SacredExecutableSessionImportLayout.PureHdBodySize)
            throw new InvalidDataException("Truncated original session importer.");
        var gold = at <= image.Length - SacredExecutableSessionImportLayout.BodySize &&
            image[at + SacredExecutableSessionImportLayout.BodySize - 1] == 0xc3 &&
            image[at + SacredExecutableSessionImportLayout.MaskedSuffixOperand - 1] == 0xbf;
        var body = image.AsSpan(at, gold ? SacredExecutableSessionImportLayout.BodySize : SacredExecutableSessionImportLayout.PureHdBodySize).ToArray();
        if (!body.AsSpan(0, 3).SequenceEqual<byte>([0x6a, 0xff, 0x68]) || body[^1] != 0xc3)
            throw new InvalidDataException("Unknown original session importer extent.");
        var mask = Text(gold ? SacredExecutableSessionImportLayout.MaskedSuffixOperand : SacredExecutableSessionImportLayout.PureHdMaskedSuffixOperand, 0xbf);
        var secondMask = Text(gold ? SacredExecutableSessionImportLayout.SecondMaskedSuffixOperand : SacredExecutableSessionImportLayout.PureHdSecondMaskedSuffixOperand, 0xbf);
        if (!mask.AsSpan().SequenceEqual(secondMask))
            throw new InvalidDataException("Independent CD-key masks require separate definitions.");
        return new(Text(SacredExecutableSessionImportLayout.SessionDataKeyOperand, 0x68),
            Text(SacredExecutableSessionImportLayout.CharacterKeyOperand, 0x68), mask);

        byte[] Text(int operand, byte opcode)
        {
            if (body[operand - 1] != opcode) throw new InvalidDataException("Unknown original importer literal operand.");
            var address = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(operand));
            if (address < imageBase) throw new InvalidDataException("Importer literal precedes executable.");
            var rva = (ulong)address - imageBase;
            foreach (var section in headers.SectionHeaders)
            {
                if (rva < (uint)section.VirtualAddress) continue;
                var relative = rva - (uint)section.VirtualAddress;
                if (relative >= (uint)section.SizeOfRawData) continue;
                var position = checked(section.PointerToRawData + (int)relative);
                var remaining = Math.Min(section.SizeOfRawData - (int)relative, image.Length - position);
                if (position < 0 || remaining < 0) break;
                var bytes = image.AsSpan(position, remaining); var end = bytes.IndexOf((byte)0);
                if (end >= 0) return bytes[..end].ToArray();
                break;
            }
            throw new InvalidDataException("Original importer literal exceeds stored section bounds.");
        }
    }

    public static SacredSessionImportDefinition Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original session-import definitions unavailable.", failure);
    }
}
