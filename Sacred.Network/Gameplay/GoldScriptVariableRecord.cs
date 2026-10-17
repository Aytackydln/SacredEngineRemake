using System.Buffers.Binary;
using System.Text;

namespace Sacred.Network.Gameplay;

public sealed class GoldScriptVariableRecord
{
    private readonly byte[] _data;
    private GoldScriptVariableRecord(byte[] data) => _data = data;
    public int Value => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(GoldScriptVariableLayout.ValueOffset));
    public ReadOnlySpan<byte> NameStorage => _data.AsSpan(0, GoldScriptVariableLayout.NameSize);
    public ReadOnlySpan<byte> NameBytes
    {
        get
        {
            var end = NameStorage.IndexOf((byte)0);
            return end >= 0 ? NameStorage[..end] : NameStorage;
        }
    }
    public string Name => Encoding.Latin1.GetString(NameBytes);
    public byte[] ToPayload() => _data.ToArray();
    /// <summary>SetVar changes the value in place; it does not rewrite the stored name or its suffix bytes.</summary>
    public GoldScriptVariableRecord WithValue(int value)
    {
        var bytes = _data.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldScriptVariableLayout.ValueOffset), value);
        return new(bytes);
    }
    public static GoldScriptVariableRecord FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != GoldScriptVariableLayout.RecordSize) throw new InvalidDataException("Script variable requires 36 bytes.");
        return new(bytes.ToArray());
    }
    public static GoldScriptVariableRecord Create(ReadOnlySpan<byte> name, int value)
    {
        if (name.Length >= GoldScriptVariableLayout.NameSize || name.IndexOf((byte)0) >= 0)
            throw new ArgumentException("Variable name must fit the native 32-byte NUL-terminated storage.", nameof(name));
        var bytes = new byte[GoldScriptVariableLayout.RecordSize];
        name.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldScriptVariableLayout.ValueOffset), value);
        return new(bytes);
    }
    /// <summary>Native length check and default-C-locale _stricmp; high bytes retain their identity.</summary>
    public bool MatchesName(ReadOnlySpan<byte> name)
    {
        if (NameBytes.Length != name.Length) return false;
        for (var i = 0; i < name.Length; i++) if (Fold(NameBytes[i]) != Fold(name[i])) return false;
        return true;
        static byte Fold(byte value) => value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
    }
}
