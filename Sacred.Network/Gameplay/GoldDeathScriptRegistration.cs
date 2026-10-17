using System.Buffers.Binary;
using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable native death-script registration; the text is retained verbatim, never executed here.</summary>
public sealed class GoldDeathScriptRegistration
{
    private readonly byte[] _payload;
    private GoldDeathScriptRegistration(byte[] payload) => _payload = payload;

    public uint ObjectReference => BinaryPrimitives.ReadUInt32LittleEndian(_payload);
    public ReadOnlySpan<byte> ScriptBytes => _payload.AsSpan(GoldDeathScriptLayout.ScriptTextOffset, GoldDeathScriptLayout.ScriptTextSize);
    /// <summary>Byte-preserving Latin-1 diagnostic view through the first NUL. The original bytes own the wire identity.</summary>
    public string ScriptText
    {
        get
        {
            var bytes = ScriptBytes;
            var end = bytes.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
        }
    }

    public static GoldDeathScriptRegistration FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldDeathScriptLayout.Size)
            throw new InvalidDataException("Gold death-script registration requires exactly 68 bytes.");
        return new(payload.ToArray());
    }

    /// <summary>Supply the original 64-byte field, including its padding. No truncation or text conversion is applied.</summary>
    public static GoldDeathScriptRegistration Create(uint objectReference, ReadOnlySpan<byte> scriptBytes)
    {
        if (scriptBytes.Length != GoldDeathScriptLayout.ScriptTextSize)
            throw new ArgumentException("A native death-script text field has exactly 64 bytes.", nameof(scriptBytes));
        var payload = new byte[GoldDeathScriptLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, objectReference);
        scriptBytes.CopyTo(payload.AsSpan(GoldDeathScriptLayout.ScriptTextOffset));
        return new(payload);
    }

    public byte[] ToPayload() => _payload.ToArray();
}
