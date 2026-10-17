using System.Buffers.Binary;
using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>Bank 2 section 3: native +0x358 vector, one object ID and 64 callback-name bytes.
/// Naming a callback registers it; it does not invoke the function.</summary>
public sealed class GoldObjectScriptCallback
{
    public const int Size = 68;
    private readonly byte[] _payload;
    private GoldObjectScriptCallback(byte[] payload) => _payload = payload;
    public uint ObjectReference => BinaryPrimitives.ReadUInt32LittleEndian(_payload);
    public string Name => Encoding.Latin1.GetString(_payload.AsSpan(4).Slice(0, _payload.AsSpan(4).IndexOf((byte)0)));
    public static GoldObjectScriptCallback Create(uint reference, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (reference is < GoldReferenceCodec.FirstObjectReference or >= GoldReferenceCodec.ReferenceLimit)
            throw new ArgumentOutOfRangeException(nameof(reference));
        if (name.Length >= 64 || name.Contains('\0') || name.Any(ch => ch > 255))
            throw new ArgumentException("Callback name requires at most 63 Latin-1 bytes.", nameof(name));
        var payload = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        Encoding.Latin1.GetBytes(name, payload.AsSpan(4));
        return new(payload);
    }
    public static GoldObjectScriptCallback FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != Size || !payload[4..].Contains((byte)0))
            throw new InvalidDataException("Invalid 68-byte object callback record.");
        return new(payload.ToArray());
    }
    public byte[] ToPayload() => _payload.ToArray();
}
