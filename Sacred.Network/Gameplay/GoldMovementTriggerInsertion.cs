using System.Buffers.Binary;
using System.Text;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold SetBaseTrigger publication. The sector word is used by the server; the client routes by coordinates.</summary>
public sealed class GoldMovementTriggerInsertion
{
    private readonly byte[] _data;
    private GoldMovementTriggerInsertion(byte[] data) => _data = data;
    public uint Sector => BinaryPrimitives.ReadUInt32LittleEndian(_data);
    public int FirstX => Word(4);
    public int FirstY => Word(8);
    public int Surface => Word(12);
    public int LastX => Word(16);
    public int LastY => Word(20);
    /// <summary>Producer copies up to 79 name bytes here. Native sector storage can safely hold only 63 plus NUL.</summary>
    public ReadOnlySpan<byte> NameStorage => _data.AsSpan(24, 80);
    public ReadOnlySpan<byte> NameBytes => NameStorage[..NameStorage.IndexOf((byte)0)];
    public string Name => Encoding.Latin1.GetString(NameBytes);
    public GoldMovementTriggerNameRecord Registration => GoldMovementTriggerNameRecord.FromPayload(_data.AsSpan(104, 128));
    public byte[] ToPayload() => _data.ToArray();
    public static GoldMovementTriggerInsertion FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldMovementTriggerInsertionCodec.Size)
            throw new InvalidDataException("Gold movement-trigger insertion requires exactly 232 bytes.");
        // The original append uses an unbounded strcpy into a 64-byte field. Reject native buffer-overflow inputs.
        if (payload.Slice(24, GoldMovementTriggerLayout.NameSize).IndexOf((byte)0) < 0)
            throw new InvalidDataException("Gold movement-trigger name exceeds its native sector storage.");
        return new(payload.ToArray());
    }
    private int Word(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset));
}

public sealed record GoldMovementTriggerInsertionEvent(GoldMessage Message, GoldMovementTriggerInsertion Insertion) : GoldGameEvent(Message);

public static class GoldMovementTriggerInsertionCodec
{
    public const ushort Opcode = 0x1C3;
    public const int Size = 232;
    public static GoldMovementTriggerInsertionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode) throw new InvalidDataException("Not a Gold movement-trigger insertion.");
        var insertion = GoldMovementTriggerInsertion.FromPayload(message.Payload);
        return new(message with { Payload = insertion.ToPayload() }, insertion);
    }
}
