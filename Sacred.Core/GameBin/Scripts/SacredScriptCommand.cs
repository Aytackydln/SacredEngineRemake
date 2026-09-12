using System.Buffers.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>An instruction and its original bytes, including unrecognized operands.
/// The backing memory must remain unchanged while the command is in use.</summary>
public readonly record struct SacredScriptCommand(int FileOffset, ReadOnlyMemory<byte> Bytes)
{
    public ushort Opcode => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.Span);
}
