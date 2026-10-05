using System.Buffers.Binary;
using System.Text;
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
            var name = row[..64];
            var end = name.IndexOf((byte)0);
            if (end >= 0) name = name[..end];
            var start = BinaryPrimitives.ReadInt32LittleEndian(row[64..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(row[68..]);
            if (start < 0 || length < 0 || (long)start + length > codeLength)
                throw new InvalidDataException($"Vectoren.bin function {i} exceeds FunkCode.bin.");
            result[i] = new(Encoding.Latin1.GetString(name), start, length,
                BinaryPrimitives.ReadInt32LittleEndian(row[72..]), BinaryPrimitives.ReadInt32LittleEndian(row[76..]),
                BinaryPrimitives.ReadUInt32LittleEndian(row[80..]));
        }
        return result;
    }
}
