using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldInterpreterVector(uint Key, ReadOnlyMemory<byte> Records);
public sealed record GoldInterpreterStateEvent(GoldMessage Message, ReadOnlyMemory<byte> State,
    IReadOnlyList<GoldInterpreterVector> Vectors1320, ReadOnlyMemory<byte> Map132C,
    ReadOnlyMemory<byte> Map1338, ReadOnlyMemory<byte> Map1344, ReadOnlyMemory<byte> Vector1350,
    IReadOnlyList<GoldInterpreterVector> Vectors135C, ReadOnlyMemory<byte> RemainingData) : GoldReceiveWorkEvent(Message);

/// <summary>7F9650's memory-file read order. Runtime map offsets are names, not guessed gameplay meanings.</summary>
public static class GoldInterpreterSnapshot
{
    public const ushort Opcode = 0x1AA;
    public static GoldInterpreterStateEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode) throw new InvalidDataException("Not a Gold interpreter transfer.");
        var owned = message with { Payload = message.Payload.ToArray() };
        var reader = new GoldWorldBankReader(owned.Payload, 0);
        var size = reader.ReadCount(); reader.RequireRecords(size, 1); var state = reader.ReadBlock((int)size);
        var count = reader.ReadCount(); reader.RequireRecords(count, 8); var first = new GoldInterpreterVector[(int)count];
        for (var i = 0; i < first.Length; i++) { var key = reader.ReadCount(); first[i] = new(key, reader.ReadVector(12)); }
        var map132C = reader.ReadVector(8); var map1338 = reader.ReadVector(8);
        var map1344 = reader.ReadVector(32); var vector1350 = reader.ReadVector(16);
        count = reader.ReadCount(); reader.RequireRecords(count, 8); var last = new GoldInterpreterVector[(int)count];
        for (var i = 0; i < last.Length; i++) { var key = reader.ReadCount(); last[i] = new(key, reader.ReadVector(4)); }
        return new(owned, state, Array.AsReadOnly(first), map132C, map1338, map1344, vector1350,
            Array.AsReadOnly(last), owned.Payload.AsMemory(reader.Cursor));
    }
}
