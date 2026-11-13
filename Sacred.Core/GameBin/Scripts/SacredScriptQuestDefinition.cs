using System.Buffers.Binary;
using System.Text;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Complete original quest row, retaining unknown fields and string padding.</summary>
public sealed class SacredScriptQuestDefinition
{
    private readonly byte[] _bytes;
    private SacredScriptQuestDefinition(byte[] bytes) => _bytes = bytes;
    public int Number => BinaryPrimitives.ReadInt32LittleEndian(_bytes);
    public string Name
    {
        get
        {
            var name = _bytes.AsSpan(SacredScriptQuestLayout.NameOffset, SacredScriptQuestLayout.NameLength);
            var end = name.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? name : name[..end]);
        }
    }
    public int EnterFunction => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(SacredScriptQuestLayout.EnterFunctionOffset));
    public int SetupFunction => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(SacredScriptQuestLayout.SetupFunctionOffset));
    public int ActivationFunction => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(SacredScriptQuestLayout.ActivationFunctionOffset));
    public uint Flags => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(SacredScriptQuestLayout.FlagsOffset));
    public byte[] ToPayload() => _bytes.ToArray();
    public static SacredScriptQuestDefinition FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredScriptQuestLayout.SerializedSize) throw new InvalidDataException("Quest definition requires 292 bytes.");
        return new(bytes.ToArray());
    }
}
