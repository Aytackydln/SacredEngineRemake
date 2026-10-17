using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Gold's flat byte-offset branch scanner. The caller evaluates predicates and executes yielded commands.
/// This deliberately does not introduce a conventional nested IF stack absent from the traced native scanner.</summary>
public static class SacredScriptControlFlowReader
{
    public static IEnumerable<SacredScriptCommand> Read(ReadOnlyMemory<byte> code, Func<SacredScriptCommand, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var offset = 0;
        while (offset < code.Length)
        {
            var command = Take(SacredScriptControlOpcodes.MaximumInstructionBytes);
            if (command.Opcode == SacredScriptControlOpcodes.Stop) yield break;
            if (command.Opcode == SacredScriptControlOpcodes.If)
            {
                if (!predicate(command))
                    while (true)
                    {
                        var candidate = Scan();
                        if (candidate.Opcode == SacredScriptControlOpcodes.BranchEnd ||
                            candidate.Opcode == SacredScriptControlOpcodes.ElseIf && predicate(candidate)) break;
                    }
            }
            else if (command.Opcode is SacredScriptControlOpcodes.ElseIf or SacredScriptControlOpcodes.BranchEnd)
            {
                if (command.Opcode == SacredScriptControlOpcodes.ElseIf)
                    while (Scan().Opcode != SacredScriptControlOpcodes.BranchEnd) { }
                // Both native paths discard one complete instruction after opcode 59,
                // normally the compiler's NOP. Do not search for a later marker instead.
                Take(SacredScriptControlOpcodes.MaximumScannedInstructionBytes);
            }
            else yield return command;
        }

        SacredScriptCommand Scan()
        {
            var command = Take(SacredScriptControlOpcodes.MaximumScannedInstructionBytes);
            if (command.Opcode is 0 or > SacredScriptControlOpcodes.MaximumOpcode)
                throw new InvalidDataException($"Invalid opcode in native branch scan at 0x{command.FileOffset:X}.");
            return command;
        }
        SacredScriptCommand Take(int maximum)
        {
            if (code.Length - offset < SacredScriptCommandHeaderLayout.SerializedSize)
                throw new InvalidDataException($"Missing script instruction at 0x{offset:X}.");
            var length = BinaryPrimitives.ReadUInt16LittleEndian(code.Span[(offset + 2)..]);
            if (length < SacredScriptCommandHeaderLayout.SerializedSize || length > maximum || length > code.Length - offset)
                throw new InvalidDataException($"Invalid script instruction length {length} at 0x{offset:X}.");
            var command = new SacredScriptCommand(offset, code.Slice(offset, length));
            offset += length;
            return command;
        }
    }
}
