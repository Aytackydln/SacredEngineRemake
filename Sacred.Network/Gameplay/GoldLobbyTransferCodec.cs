using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldLobbyTransferEvent(GoldMessage Message, uint RequestWord, uint Selector, uint? StatusWord,
    ReadOnlyMemory<byte>? DecodedProfile, ReadOnlyMemory<byte>? PlayerProfile) : GoldLobbyEvent(Message);

public static class GoldLobbyTransferCodec
{
    public const ushort QueryOpcode = 0x029;
    public const ushort UpdateOpcode = 0x02B;
    public static GoldLobbyTransferEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (QueryOpcode or UpdateOpcode)) throw new InvalidDataException("Not a Gold profile transfer.");
        if (message.Payload.Length < 8) throw new InvalidDataException("Truncated Gold profile transfer header.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        var selector = Word(4);
        if (message.Opcode == QueryOpcode && selector != 10) return new(owned, Word(0), selector, null, null, null);
        if (owned.Payload.Length < 16) throw new InvalidDataException("Truncated Gold transfer status.");
        var status = Word(12);
        var takesProfile = selector == 10 && (message.Opcode == QueryOpcode ? status != 0 : status > 1);
        ReadOnlyMemory<byte>? decoded = null;
        if (takesProfile) decoded = GoldPackedProfileBlock.Decode(owned.Payload.AsSpan(16));
        ReadOnlyMemory<byte>? player = null;
        if (message.Opcode == UpdateOpcode && selector != 10 && status == 0x230)
        {
            if (owned.Payload.Length < 16 + 556) throw new InvalidDataException("Truncated Gold player profile.");
            player = owned.Payload.AsMemory(16, 556);
        }
        return new(owned, Word(0), selector, status, decoded, player);
    }
}
