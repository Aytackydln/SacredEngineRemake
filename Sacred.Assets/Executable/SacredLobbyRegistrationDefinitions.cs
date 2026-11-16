using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

/// <summary>Read original lobby registration operands and referenced ANSI strings.
/// No registry key/value defaults or writes are owned by this reader.</summary>
public static class SacredLobbyRegistrationDefinitions
{
    public static SacredLobbyRegistrationDefinition Read(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing executable image base.");
        var signature = SacredExecutableLobbyRegistrationLayout.Signature;
        var at = image.AsSpan().IndexOf(signature);
        if (at < 0 || at > image.Length - SacredExecutableLobbyRegistrationLayout.BodySize || image.AsSpan(at + signature.Length).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original lobby registration block missing or ambiguous.");
        var body = image.AsSpan(at, SacredExecutableLobbyRegistrationLayout.BodySize).ToArray();
        var application = Text(SacredExecutableLobbyRegistrationLayout.ApplicationNameOperand);
        var command = Text(SacredExecutableLobbyRegistrationLayout.EmptyCommandOperand);
        var description = Text(SacredExecutableLobbyRegistrationLayout.DescriptionOperand);
        var launcher = Text(SacredExecutableLobbyRegistrationLayout.LauncherPrefixOperand)
            .Concat(Text(SacredExecutableLobbyRegistrationLayout.LauncherSuffixOperand)).ToArray();
        var guid = Text(SacredExecutableLobbyRegistrationLayout.GuidOperand);
        ReadOnlyMemory<byte>?[] literals = [application, command, null, description, null, launcher, null, guid];
        var operands = SacredExecutableLobbyRegistrationLayout.ValueNameOperands;
        var values = new SacredLobbyRegistrationValue[operands.Length];
        for (var index = 0; index < operands.Length; index++)
        {
            var operand = operands[index];
            if (body[operand - 4] != 0x6a || body[operand - 2] != 0x53 || body[operand - 1] != 0x68)
                throw new InvalidDataException("Unknown original registration value/type operands.");
            values[index] = new(Text(operand), SignedImmediate(operand + SacredExecutableLobbyRegistrationLayout.ValueTypeOperandDistance), literals[index]);
        }
        return new(Word(SacredExecutableLobbyRegistrationLayout.RootKeyOperand),
            SignedImmediate(SacredExecutableLobbyRegistrationLayout.CreateAccessOperand), Word(SacredExecutableLobbyRegistrationLayout.DirectoryCapacityOperand),
            Text(SacredExecutableLobbyRegistrationLayout.KeyPathOperand), values);

        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset));
        uint SignedImmediate(int offset) => unchecked((uint)(sbyte)body[offset]);
        byte[] Text(int operand)
        {
            if (body[operand - 1] is not (0x68 or 0xbf)) throw new InvalidDataException("Unknown original ANSI operand.");
            var address = Word(operand);
            if (address < imageBase) throw new InvalidDataException("ANSI operand precedes image.");
            var rva = (ulong)address - imageBase;
            foreach (var section in headers.SectionHeaders)
            {
                if (rva < (uint)section.VirtualAddress) continue;
                var relative = rva - (uint)section.VirtualAddress;
                if (relative >= (uint)Math.Max(section.VirtualSize, section.SizeOfRawData)) continue;
                // PE loader initializes unstored virtual data to zero.
                if (relative >= (uint)section.SizeOfRawData) return [];
                var offset = checked(section.PointerToRawData + (int)relative);
                var remaining = Math.Min(section.SizeOfRawData - (int)relative, image.Length - offset);
                if (offset < 0 || remaining < 0) break;
                var bytes = image.AsSpan(offset, remaining);
                var end = bytes.IndexOf((byte)0);
                if (end >= 0) return bytes[..end].ToArray();
                if (section.VirtualSize > section.SizeOfRawData) return bytes.ToArray();
                break;
            }
            throw new InvalidDataException("Original ANSI string exceeds stored/zero-filled section bounds.");
        }
    }

    public static SacredLobbyRegistrationDefinition Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original lobby registration definitions unavailable.", failure);
    }
}
