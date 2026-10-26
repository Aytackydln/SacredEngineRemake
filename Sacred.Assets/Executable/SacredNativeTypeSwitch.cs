using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace Sacred.Assets.Executable;

/// <summary>Reads a native constant-return type switch. This bounded evaluator
/// accepts only register comparisons, branches and reads of embedded switch
/// tables: no calls, native execution or memory writes are permitted.</summary>
internal sealed class SacredNativeTypeSwitch
{
    private readonly byte[] _image;
    private readonly SectionHeader[] _sections;
    private readonly ulong _imageBase;
    public SacredNativeTypeSwitch(byte[] image)
    {
        _image = image.ToArray();
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        _sections = pe.PEHeaders.SectionHeaders.ToArray();
        _imageBase = pe.PEHeaders.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing PE header.");
    }
    public ReadOnlySpan<byte> At(ulong address, int count)
    {
        if (address >= _imageBase)
            foreach (var section in _sections)
                if (address - _imageBase >= (ulong)section.VirtualAddress && address - _imageBase + (ulong)count <= (ulong)section.VirtualAddress + (ulong)section.SizeOfRawData)
                    return _image.AsSpan(checked(section.PointerToRawData + (int)(address - _imageBase - (ulong)section.VirtualAddress)), count);
        throw new InvalidDataException("Native switch read lies outside the executable.");
    }
    public ulong Address(int offset)
    {
        foreach (var section in _sections)
            if (offset >= section.PointerToRawData && offset < section.PointerToRawData + section.SizeOfRawData)
                return _imageBase + (ulong)(section.VirtualAddress + offset - section.PointerToRawData);
        throw new InvalidDataException("Native switch instruction lies outside executable sections.");
    }
    public uint Read(ulong address, uint type)
    {
        uint eax = 0, ecx = 0, edx = 0, left = 0, right = 0;
        for (var step = 0; step < 64; step++)
        {
            var code = At(address, 16);
            if (code[..4].SequenceEqual<byte>([0x8B, 0x4C, 0x24, 4])) { ecx = type; address += 4; }
            else if (code[..4].SequenceEqual<byte>([0x8B, 0x44, 0x24, 4])) { eax = type; address += 4; }
            else if (code[..2].SequenceEqual<byte>([0x33, 0xC0])) { eax = 0; address += 2; }
            else if (code[..2].SequenceEqual<byte>([0x33, 0xC9])) { ecx = 0; address += 2; }
            else if (code[..2].SequenceEqual<byte>([0x33, 0xD2])) { edx = 0; address += 2; }
            else if (code[0] == 0xB8) { eax = Word(code[1..]); address += 5; }
            else if (code[0] == 0xB0) { eax = (eax & 0xFFFFFF00) | code[1]; address += 2; }
            else if (code[..2].SequenceEqual<byte>([0x32, 0xC0])) { eax &= 0xFFFFFF00; address += 2; }
            else if (code[0] == 0x3D) { left = eax; right = Word(code[1..]); address += 5; }
            else if (code[..2].SequenceEqual<byte>([0x83, 0xF8])) { left = eax; right = unchecked((uint)(sbyte)code[2]); address += 3; }
            else if (code[0] == 0x48) { eax = unchecked(eax - 1); left = eax; right = 0; address++; }
            else if (code[0] == 0x05) { eax = unchecked(eax + Word(code[1..])); left = eax; right = 0; address += 5; }
            else if (code[..2].SequenceEqual<byte>([0x83, 0xC0])) { eax = unchecked(eax + (uint)(sbyte)code[2]); left = eax; right = 0; address += 3; }
            else if (code[..2].SequenceEqual<byte>([0x81, 0xF9])) { left = ecx; right = Word(code[2..]); address += 6; }
            else if (code[..2].SequenceEqual<byte>([0x83, 0xF9])) { left = ecx; right = unchecked((uint)(sbyte)code[2]); address += 3; }
            else if (code[..2].SequenceEqual<byte>([0x81, 0xC1])) { ecx = unchecked(ecx + Word(code[2..])); address += 6; }
            else if (code[..2].SequenceEqual<byte>([0x83, 0xC1])) { ecx = unchecked(ecx + (uint)(sbyte)code[2]); address += 3; }
            else if (code[..2].SequenceEqual<byte>([0x8A, 0x91])) { edx = At(Word(code[2..]) + ecx, 1)[0]; address += 6; }
            else if (code[..2].SequenceEqual<byte>([0x8A, 0x88])) { ecx = At(Word(code[2..]) + eax, 1)[0]; address += 6; }
            else if (code[..2].SequenceEqual<byte>([0x8A, 0x90])) { edx = At(Word(code[2..]) + eax, 1)[0]; address += 6; }
            else if (code[..3].SequenceEqual<byte>([0xFF, 0x24, 0x95])) address = Word(At(Word(code[3..]) + edx * 4, 4));
            else if (code[..3].SequenceEqual<byte>([0xFF, 0x24, 0x85])) address = Word(At(Word(code[3..]) + eax * 4, 4));
            else if (code[..3].SequenceEqual<byte>([0xFF, 0x24, 0x8D])) address = Word(At(Word(code[3..]) + ecx * 4, 4));
            else if (code[0] == 0xC2 && code[2] == 0 && code[1] is 4 or 8) return eax;
            else if (code[0] == 0xC3) return eax;
            else if (code[0] is 0x73 or 0x74 or 0x75 or 0x77 or 0x7D or 0x7F)
                address = unchecked((ulong)((long)address + 2 + (Branch(code[0]) ? (sbyte)code[1] : 0)));
            else if (code[0] == 0x0F && code[1] is 0x83 or 0x84 or 0x85 or 0x87 or 0x8D or 0x8F)
                address = unchecked((ulong)((long)address + 6 + (Branch((byte)(code[1] - 0x10)) ? BinaryPrimitives.ReadInt32LittleEndian(code[2..]) : 0)));
            else throw new InvalidDataException($"Unsupported instruction in native type switch at 0x{address:X}.");
        }
        throw new InvalidDataException("Native type switch exceeds its bounded branch count.");
        bool Branch(byte op) => op switch
        {
            0x73 => left >= right,
            0x74 => left == right,
            0x75 => left != right,
            0x77 => left > right,
            0x7D => (int)left >= (int)right,
            0x7F => (int)left > (int)right,
            _ => throw new InvalidDataException("Unsupported type-switch condition.")
        };
    }
    private static uint Word(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
