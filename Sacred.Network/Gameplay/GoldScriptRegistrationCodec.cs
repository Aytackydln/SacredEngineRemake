using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldScriptRegistrationEvent(GoldMessage Message) : GoldScriptWorldEvent(Message);
public sealed record GoldSpawnRenewEvent(GoldMessage Message, GoldSpawnRenewRecord Record) : GoldScriptRegistrationEvent(Message);
public sealed record GoldMouseoverEvent(GoldMessage Message, GoldSectorScriptPatch Record) : GoldScriptRegistrationEvent(Message);

public static class GoldScriptRegistrationCodec
{
    public static GoldScriptRegistrationEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            GoldScriptRegistrationLayout.SpawnRenewOpcode => GoldScriptRegistrationLayout.SpawnRenewSize,
            GoldScriptRegistrationLayout.MouseoverOpcode => GoldSectorScriptPatch.Size,
            _ => throw new InvalidDataException("Expected a Gold spawn-renew or mouseover registration.")
        };
        if (message.Payload.Length < size) throw new InvalidDataException($"Gold registration requires {size} bytes.");
        var copy = message with { Payload = message.Payload.ToArray() };
        return message.Opcode == GoldScriptRegistrationLayout.SpawnRenewOpcode
            ? new GoldSpawnRenewEvent(copy, GoldSpawnRenewRecord.FromPayload(copy.Payload.AsSpan(0, size)))
            : new GoldMouseoverEvent(copy, GoldSectorScriptPatch.FromPayload(copy.Payload.AsSpan(0, size)));
    }
    public static GoldMessage CreateSpawnRenew(byte slot, GoldSpawnRenewRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Create(slot, GoldScriptRegistrationLayout.SpawnRenewOpcode, record.ToPayload());
    }
    public static GoldMessage CreateMouseover(byte slot, GoldSectorScriptPatch record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Create(slot, GoldScriptRegistrationLayout.MouseoverOpcode, record.ToPayload());
    }
    private static GoldMessage Create(byte slot, ushort opcode, byte[] bytes)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        return new(opcode, slot, slot, 0, bytes);
    }
}
