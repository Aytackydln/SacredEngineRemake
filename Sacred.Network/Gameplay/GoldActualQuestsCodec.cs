using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldActualQuestsEvent(GoldMessage Message) : GoldScriptWorldEvent(Message);
public sealed record GoldActualQuestsResetEvent(GoldMessage Message, uint QuestWord) : GoldActualQuestsEvent(Message);
public sealed record GoldActualQuestsSnapshotEvent(GoldMessage Message) : GoldActualQuestsEvent(Message);

public static class GoldActualQuestsCodec
{
    public static GoldActualQuestsEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message.Opcode)
        {
            case GoldActualQuestsLayout.ResetOpcode when message.Payload.Length >= sizeof(uint):
                var copy = message with { Payload = message.Payload.ToArray() };
                return new GoldActualQuestsResetEvent(copy, BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload));
            case GoldActualQuestsLayout.FullOpcode when message.Payload.Length <= GoldActualQuestsLayout.Size:
                return new GoldActualQuestsSnapshotEvent(message with { Payload = message.Payload.ToArray() });
            default:
                throw new InvalidDataException("Gold actual quests requires a reset word or a table prefix of at most 128 bytes.");
        }
    }

    /// <summary>The native server accepts and relays 0x1C9; it does not accept 0x1CA uploads.</summary>
    public static GoldMessage CreateReset(byte slot, uint questWord)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, questWord);
        return new(GoldActualQuestsLayout.ResetOpcode, slot, slot, 0, bytes);
    }
}
