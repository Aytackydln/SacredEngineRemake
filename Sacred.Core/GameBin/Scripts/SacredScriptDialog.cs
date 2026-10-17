using System.Buffers.Binary;
using System.Text;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Immutable original dialog record; its function word remains in the source representation.</summary>
public sealed class SacredScriptDialog
{
    private readonly byte[] _bytes;
    private SacredScriptDialog(byte[] bytes) => _bytes = bytes;
    public int Number => BinaryPrimitives.ReadInt32LittleEndian(_bytes);
    public string Name
    {
        get
        {
            var bytes = _bytes.AsSpan(SacredScriptDialogLayout.NameOffset, SacredScriptDialogLayout.NameLength);
            var end = bytes.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
        }
    }
    public uint Function => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(SacredScriptDialogLayout.FunctionOffset));
    public uint Icon => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(SacredScriptDialogLayout.IconOffset));
    public uint AttachedObjectReference => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(SacredScriptDialogLayout.AttachedObjectReferenceOffset));
    public byte[] ToPayload() => _bytes.ToArray();
    public static SacredScriptDialog FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredScriptDialogLayout.SerializedSize) throw new InvalidDataException("Dialog record requires 80 bytes.");
        return new(bytes.ToArray());
    }
}
