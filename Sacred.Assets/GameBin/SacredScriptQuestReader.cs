using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Vectoren.bin's second counted table, directly copied by Gold 0x470054.
/// Initial flags and callbacks come from the file; later pool tables remain untouched.</summary>
public static class SacredScriptQuestReader
{
    public static IReadOnlyList<SacredScriptQuestDefinition> Read(ReadOnlySpan<byte> vectors)
    {
        if (vectors.Length < 4) throw new InvalidDataException("Truncated Vectoren.bin function count.");
        var offset = 4L + (long)BinaryPrimitives.ReadUInt32LittleEndian(vectors) * SacredScriptFunctionLayout.SerializedSize;
        if (offset + 4 > vectors.Length) throw new InvalidDataException("Truncated Vectoren.bin quest count.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(vectors[(int)offset..]);
        offset += 4;
        if (offset + (long)count * SacredScriptQuestLayout.SerializedSize > vectors.Length)
            throw new InvalidDataException("Truncated Vectoren.bin quest table.");
        var result = new SacredScriptQuestDefinition[checked((int)count)];
        for (var index = 0; index < result.Length; index++)
            result[index] = SacredScriptQuestDefinition.FromPayload(vectors.Slice((int)offset + index * SacredScriptQuestLayout.SerializedSize, SacredScriptQuestLayout.SerializedSize));
        return Array.AsReadOnly(result);
    }
}
