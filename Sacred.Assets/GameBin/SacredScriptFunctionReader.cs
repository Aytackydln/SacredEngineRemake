using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

public readonly record struct SacredScriptFunction(string Name, int Start, int ByteLength, int Quest, int Number, uint Deleted);

/// <summary>Reads only the function table; subsequent Vectoren.bin tables remain untouched.</summary>
public static class SacredScriptFunctionReader
{
    public static IReadOnlyList<SacredScriptFunction> Read(ReadOnlySpan<byte> data, int codeLength)
    {
        if (data.Length < 4) throw new InvalidDataException("Truncated Vectoren.bin count.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (4L + (long)count * SacredScriptFunctionLayout.SerializedSize > data.Length)
            throw new InvalidDataException("Truncated Vectoren.bin function table.");
        var result = new SacredScriptFunction[checked((int)count)];
        for (var i = 0; i < result.Length; i++)
        {
            var row = data.Slice(4 + i * SacredScriptFunctionLayout.SerializedSize, SacredScriptFunctionLayout.SerializedSize);
            var definition = SacredScriptFunctionDefinition.FromPayload(row);
            if (definition.Start < 0 || definition.ByteLength < 0 || (long)definition.Start + definition.ByteLength > codeLength)
                throw new InvalidDataException($"Vectoren.bin function {i} exceeds FunkCode.bin.");
            result[i] = new(definition.Name, definition.Start, definition.ByteLength, definition.Quest, definition.Number, definition.Deleted);
        }
        return result;
    }
}
