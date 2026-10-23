using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>128-byte movement-trigger name registration, bank 2 section 1 and 0x1C3 trailer. All bytes are retained.</summary>
public sealed class GoldMovementTriggerNameRecord
{
    private readonly byte[] _data;
    private GoldMovementTriggerNameRecord(byte[] data) => _data = data;
    public ReadOnlySpan<byte> NameStorage => _data;
    public string Name
    {
        get
        {
            var end = NameStorage.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? NameStorage : NameStorage[..end]);
        }
    }
    public byte[] ToPayload() => _data.ToArray();
    public static GoldMovementTriggerNameRecord FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 128) throw new InvalidDataException("Gold movement-trigger name registration requires 128 bytes.");
        return new(payload.ToArray());
    }
}
