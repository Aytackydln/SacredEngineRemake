using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Original actor-source aliases inside Gold 0x43CA20. Reads only the
/// bounded comparison/switch-table block; resources come from the executable.</summary>
public sealed class SacredEquipmentSourceDefinitions
{
    private readonly SacredNativeTypeSwitch _image;
    private readonly ulong _start;
    private SacredEquipmentSourceDefinitions(SacredNativeTypeSwitch image, ulong start) => (_image, _start) = (image, start);

    public static SacredEquipmentSourceDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x8B, 0x74, 0x24, 0x10, 0x81, 0xFE];
        var found = -1;
        for (var search = 0; search <= image.Length - 0xD4;)
        {
            var relative = image.AsSpan(search).IndexOf(signature);
            if (relative < 0) break;
            var at = search + relative;
            search = at + signature.Length;
            if (at > image.Length - 0xD4 || !image.AsSpan(at + 0xD0, 4).SequenceEqual<byte>([0x53, 0x8B, 0xCF, 0xE8])) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original equipment source aliases.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Original equipment source aliases unavailable.");
        var reader = new SacredNativeTypeSwitch(image);
        return new(reader, reader.Address(found));
    }

    public uint CanonicalResource(uint resource)
    {
        var pc = _start;
        uint source = resource, eax = 0, ecx = 0, edx = 0, left = 0, right = 0;
        for (var step = 0; step < 64; step++)
        {
            if (pc == _start + 0xD0) return source;
            if (pc < _start || pc >= _start + 0xD0) throw new InvalidDataException("Equipment source branch leaves its original block.");
            var code = _image.At(pc, 16);
            if (code[..4].SequenceEqual<byte>([0x8B, 0x74, 0x24, 0x10])) { source = resource; pc += 4; }
            else if (code[..2].SequenceEqual<byte>([0x81, 0xFE])) { left = source; right = Word(code[2..]); pc += 6; }
            else if (code[..2].SequenceEqual<byte>([0x8D, 0x46])) { eax = unchecked(source + (uint)(sbyte)code[2]); pc += 3; }
            else if (code[..2].SequenceEqual<byte>([0x8D, 0x86])) { eax = unchecked(source + Word(code[2..])); pc += 6; }
            else if (code[0] == 0x3D) { left = eax; right = Word(code[1..]); pc += 5; }
            else if (code[..2].SequenceEqual<byte>([0x33, 0xD2])) { edx = 0; pc += 2; }
            else if (code[..2].SequenceEqual<byte>([0x33, 0xC9])) { ecx = 0; pc += 2; }
            else if (code[..2].SequenceEqual<byte>([0x8A, 0x90])) { edx = _image.At(Word(code[2..]) + eax, 1)[0]; pc += 6; }
            else if (code[..2].SequenceEqual<byte>([0x8A, 0x88])) { ecx = _image.At(Word(code[2..]) + eax, 1)[0]; pc += 6; }
            else if (code[..3].SequenceEqual<byte>([0xFF, 0x24, 0x95])) pc = Word(_image.At(Word(code[3..]) + edx * 4, 4));
            else if (code[..3].SequenceEqual<byte>([0xFF, 0x24, 0x8D])) pc = Word(_image.At(Word(code[3..]) + ecx * 4, 4));
            else if (code[0] == 0xBE) { source = Word(code[1..]); pc += 5; }
            else if (code[..2].SequenceEqual<byte>([0x8B, 0xC6])) { eax = source; pc += 2; }
            else if (code[0] == 0x2D) { eax = unchecked(eax - Word(code[1..])); left = eax; right = 0; pc += 5; }
            else if (code[..2].SequenceEqual<byte>([0x83, 0xE8])) { eax = unchecked(eax - (uint)(sbyte)code[2]); left = eax; right = 0; pc += 3; }
            else if (code[0] == 0x48) { eax = unchecked(eax - 1); left = eax; right = 0; pc++; }
            else if (code[0] == 0xE9) pc = unchecked((ulong)((long)pc + 5 + BinaryPrimitives.ReadInt32LittleEndian(code[1..])));
            else if (code[0] == 0xEB) pc = unchecked((ulong)((long)pc + 2 + (sbyte)code[1]));
            else if (code[0] is 0x74 or 0x75 or 0x77 or 0x7F) pc = unchecked((ulong)((long)pc + 2 + (Branch(code[0]) ? (sbyte)code[1] : 0)));
            else if (code[..2].SequenceEqual<byte>([0x0F, 0x87])) pc = unchecked((ulong)((long)pc + 6 + (left > right ? BinaryPrimitives.ReadInt32LittleEndian(code[2..]) : 0)));
            else throw new InvalidDataException($"Unsupported original equipment source instruction at {pc:X}.");
        }
        throw new InvalidDataException("Original equipment source switch exceeds its bounded branch count.");
        bool Branch(byte op) => op switch { 0x74 => left == right, 0x75 => left != right, 0x77 => left > right, 0x7F => (int)left > (int)right, _ => false };
    }

    private static uint Word(ReadOnlySpan<byte> code) => BinaryPrimitives.ReadUInt32LittleEndian(code);
}
