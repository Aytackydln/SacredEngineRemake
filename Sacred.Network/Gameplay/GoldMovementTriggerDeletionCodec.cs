using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldMovementTriggerDeletionCodec
{
    public static GoldMovementTriggerDeletionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldMovementTriggerDeletionLayout.Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        return new(message, GoldMovementTriggerDeletion.FromPayload(message.Payload));
    }

    public static GoldMessage Create(byte playerSlot, GoldMovementTriggerDeletion deletion)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        return new(GoldMovementTriggerDeletionLayout.Opcode, playerSlot, playerSlot, 0, deletion.ToPayload());
    }
}
