using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

public static class SacredCrtErrorDefinitions
{
    public static SacredCrtErrorDefinition Read(byte[] image)
    {
        using var pe = new SacredSessionExecutableImage(image);
        var function = pe.Address(pe.FindUnique(SacredExecutableCrtErrorLayout.Signature) - SacredExecutableCrtErrorLayout.SignatureOffset);
        var begin = pe.Operand(function, SacredExecutableCrtErrorLayout.TableStartOperand, 0xb8);
        var end = pe.Operand(function, SacredExecutableCrtErrorLayout.TableEndOperand, 0x3d);
        if (end <= begin || end - begin > 4096 || (end - begin) % SacredExecutableCrtErrorLayout.RowSize != 0 ||
            !pe.Bytes(function + 0x3e, 3).SequenceEqual<byte>([0x8b, 0x0c, 0xf5]) ||
            pe.Word(function + SacredExecutableCrtErrorLayout.TableValueBaseOperand) != begin + 4)
            throw new InvalidDataException("Unknown original CRT error table instructions.");
        var entries = new List<KeyValuePair<uint, uint>>();
        for (var address = begin; address < end; address += SacredExecutableCrtErrorLayout.RowSize)
            entries.Add(new(pe.Word(address), pe.Word(address + 4)));
        return new(entries.AsReadOnly(), Small(SacredExecutableCrtErrorLayout.AccessMinimumOperand), Small(SacredExecutableCrtErrorLayout.AccessMaximumOperand),
            Stored(SacredExecutableCrtErrorLayout.AccessErrorOperand), Large(SacredExecutableCrtErrorLayout.ExecutableMinimumOperand),
            Large(SacredExecutableCrtErrorLayout.ExecutableMaximumOperand), Stored(SacredExecutableCrtErrorLayout.ExecutableErrorOperand), Stored(SacredExecutableCrtErrorLayout.DefaultErrorOperand));
        uint Small(int offset) { Require(offset, [0x83, 0xf9]); return unchecked((uint)(sbyte)pe.Bytes(function + (uint)offset, 1)[0]); }
        uint Large(int offset) { Require(offset, [0x81, 0xf9]); return pe.Word(function + (uint)offset); }
        uint Stored(int offset) { Require(offset, [0xc7, 0]); return pe.Word(function + (uint)offset); }
        void Require(int offset, byte[] opcode)
        {
            if (!pe.Bytes(function + (uint)offset - (uint)opcode.Length, opcode.Length).SequenceEqual(opcode))
                throw new InvalidDataException("Unknown original CRT error fallback instructions.");
        }
    }
}
