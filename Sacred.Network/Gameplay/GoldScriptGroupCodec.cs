using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldScriptGroupEvent(GoldMessage Message, uint Destination, uint Selector,
    uint? Action, uint? Group, uint? Member) : GoldScriptWorldEvent(Message);

/// <summary>Gold 0x18F: routed script-group insertion/removal. Unknown selectors/actions are ignored.</summary>
public static class GoldScriptGroupCodec
{
    public const ushort Opcode = 0x18F;

    public static GoldScriptGroupEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode) throw new InvalidDataException("Expected Gold script-group packet.");
        var bytes = message.Payload.AsSpan();
        Require(bytes, 8);
        var destination = Word(bytes, 0); var selector = Word(bytes, 4);
        uint? action = null, group = null, member = null;
        if (selector == 0)
        {
            Require(bytes, 12); action = Word(bytes, 8);
            if (action is 0 or 1)
            {
                Require(bytes, 20); group = Word(bytes, 12); member = Word(bytes, 16);
            }
        }
        return new(message with { Payload = bytes.ToArray() }, destination, selector, action, group, member);
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static void Require(ReadOnlySpan<byte> bytes, int size)
    {
        if (bytes.Length < size) throw new InvalidDataException($"Gold script-group packet requires {size} bytes for this operation.");
    }
}
