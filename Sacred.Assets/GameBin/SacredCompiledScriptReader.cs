using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Boundary-based reader for original compiled Sacred scripts.
/// It preserves unknown opcodes and does not execute script control flow.</summary>
public static class SacredCompiledScriptReader
{
    public static IEnumerable<SacredScriptCommand> Read(ReadOnlyMemory<byte> data)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < SacredScriptCommandHeaderLayout.SerializedSize)
                throw new InvalidDataException($"Truncated script header at 0x{offset:X}.");

            var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 2)..]);
            if (length < SacredScriptCommandHeaderLayout.SerializedSize || length > data.Length - offset)
                throw new InvalidDataException($"Invalid script instruction length {length} at 0x{offset:X}.");

            yield return new SacredScriptCommand(offset, data.Slice(offset, length));
            offset += length;
        }
    }
}
