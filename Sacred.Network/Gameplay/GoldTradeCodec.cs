using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldTradeEvent(GoldMessage Message, uint SenderWord, uint? Operation,
    uint? Amount, uint? SecondAmount, IReadOnlyList<uint> References, ReadOnlyMemory<byte> InventoryData)
    : GoldReceiveWorkEvent(Message)
{
    public bool MatchesSender => SenderWord == Message.Sender;
}

/// <summary>Gold 7E0005 and 7EAEE0. The sender mismatch branch never reads an operation.</summary>
public static class GoldTradeCodec
{
    public const ushort Opcode = 0x198;
    public static GoldTradeEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < 4) throw new InvalidDataException("Truncated Gold trade sender.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(at));
        void Require(int size) { if (owned.Payload.Length < size) throw new InvalidDataException("Truncated Gold trade operation."); }
        var sender = Word(0);
        if (sender != message.Sender) return new(owned, sender, null, null, null, Array.Empty<uint>(), default);
        Require(8); var op = Word(4); uint? amount = null, second = null;
        IReadOnlyList<uint> references = Array.Empty<uint>(); ReadOnlyMemory<byte> inventory = default;
        if (op is 0x26 or 0x27 or 0x2F)
        {
            Require(12); amount = Word(8);
            if (op == 0x26)
            {
                if (amount > (owned.Payload.Length - 12) / 4) throw new InvalidDataException("Truncated Gold trade reference vector.");
                var count = (int)amount.Value;
                references = Array.AsReadOnly(Enumerable.Range(0, count).Select(i => Word(12 + i * 4)).ToArray());
                inventory = owned.Payload.AsMemory(12 + count * 4); amount = null;
            }
            else if (op == 0x2F) { Require(16); second = Word(12); inventory = owned.Payload.AsMemory(16); }
        }
        // Native passes the unadjusted length to 7EAEE0 and overreads 4/8 bytes in its
        // inventory branches. Only bytes actually present in the frame are retained.
        return new(owned, sender, op, amount, second, references, inventory);
    }
}
