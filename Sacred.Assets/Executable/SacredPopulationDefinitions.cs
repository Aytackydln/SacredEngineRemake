using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Reflection.PortableExecutable;

namespace Sacred.Assets.Executable;

/// <summary>Gold FillRegion's connection-setting exclusion switch. Resource IDs,
/// range and selected cases come from the installed executable.</summary>
public sealed class SacredPopulationDefinitions
{
    private readonly FrozenSet<uint> _excluded;
    private SacredPopulationDefinitions(HashSet<uint> excluded) => _excluded = excluded.ToFrozenSet();
    public bool Excluded(uint resource) => _excluded.Contains(resource);

    public static SacredPopulationDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original population exclusion switch unavailable.", failure);
    }

    public static SacredPopulationDefinitions Read(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing PE header.");
        // mov eax,[esi]; lea edi,[eax-base]; cmp edi,limit; ja default;
        // xor eax,eax; mov al,[edi+cases]; jmp [eax*4+targets].
        for (var offset = 0; offset <= image.Length - 47; offset++)
        {
            ReadOnlySpan<byte> code = image.AsSpan(offset, 47);
            if (!code[..4].SequenceEqual<byte>([0x8B, 6, 0x8D, 0xB8]) ||
                !code.Slice(8, 2).SequenceEqual<byte>([0x83, 0xFF]) || code[11] != 0x77 ||
                !code.Slice(13, 4).SequenceEqual<byte>([0x33, 0xC0, 0x8A, 0x87]) ||
                !code.Slice(21, 3).SequenceEqual<byte>([0xFF, 0x24, 0x85]) ||
                !code.Slice(28, 6).SequenceEqual<byte>([0x42, 0x83, 0xC6, 0x0C, 0xEB, 0xDA]) ||
                !code.Slice(34, 5).SequenceEqual<byte>([0xB9, 0x63, 0, 0, 0])) continue;
            var first = unchecked((uint)-BinaryPrimitives.ReadInt32LittleEndian(code[4..]));
            var count = code[10] + 1;
            var cases = At(BinaryPrimitives.ReadUInt32LittleEndian(code[17..]), count);
            var targets = BinaryPrimitives.ReadUInt32LittleEndian(code[24..]);
            var excluded = new HashSet<uint>();
            var defaultAddress = Address(offset + 28);
            var removeAddress = Address(offset + 34);
            for (var index = 0; index < count; index++)
            {
                var target = BinaryPrimitives.ReadUInt32LittleEndian(At(targets + (uint)cases[index] * 4, 4));
                if (target == removeAddress) excluded.Add(first + (uint)index);
                else if (target != defaultAddress) throw new InvalidDataException("Unknown population switch target.");
            }
            if (excluded.Count == 0) throw new InvalidDataException("Empty population exclusion switch.");
            return new(excluded);
        }
        throw new InvalidDataException("Original population exclusion switch not found.");

        ulong Address(int offset)
        {
            foreach (var section in headers.SectionHeaders)
                if (offset >= section.PointerToRawData && offset < section.PointerToRawData + section.SizeOfRawData)
                    return imageBase + (ulong)(section.VirtualAddress + offset - section.PointerToRawData);
            throw new InvalidDataException("Population code is outside executable sections.");
        }
        ReadOnlySpan<byte> At(ulong address, int length)
        {
            if (address >= imageBase)
            {
                var rva = address - imageBase;
                foreach (var section in headers.SectionHeaders)
                    if (rva >= (ulong)section.VirtualAddress && rva + (ulong)length <= (ulong)section.VirtualAddress + (ulong)section.SizeOfRawData)
                        return image.AsSpan(checked(section.PointerToRawData + (int)(rva - (ulong)section.VirtualAddress)), length);
            }
            throw new InvalidDataException("Population switch is outside executable sections.");
        }
    }
}
