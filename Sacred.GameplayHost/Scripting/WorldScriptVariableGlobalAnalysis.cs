using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Conservative final values over every possible unresolved write destination.
/// Null means different native destinations can still produce different values.</summary>
public sealed class WorldScriptVariableGlobalAnalysis
{
    internal WorldScriptVariableGlobalAnalysis(int?[] values) => Values = Array.AsReadOnly(values);
    public IReadOnlyList<int?> Values { get; }
    public int UnknownValueCount => Values.Count(value => value is null);
    public int[] ToValues()
    {
        if (UnknownValueCount != 0)
            throw new InvalidDataException($"Variable globals retain {UnknownValueCount} ambiguous final values.");
        return Values.Select(value => value!.Value).ToArray();
    }

    /// <summary>Original interpreter save block at +0x7750 (Gold 0x467412).
    /// This is not a complete bank or a bank-1 fixed block.</summary>
    public byte[] ToSaveBlock()
    {
        var values = ToValues();
        var bytes = new byte[SacredScriptVariableGlobalsLayout.Size];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), values[i]);
        return bytes;
    }
}
