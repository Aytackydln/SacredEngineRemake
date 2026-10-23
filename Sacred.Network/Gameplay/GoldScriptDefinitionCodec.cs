using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldScriptDefinitionEvent(GoldMessage Message) : GoldScriptWorldEvent(Message);
public sealed record GoldScriptDefinitionUpdateEvent(GoldMessage Message, uint Index, SacredScriptDefinition Definition) : GoldScriptDefinitionEvent(Message);
public sealed record GoldScriptDefinitionDeleteEvent(GoldMessage Message, uint Kind, string Name) : GoldScriptDefinitionEvent(Message);

public static class GoldScriptDefinitionCodec
{
    public const ushort UpdateOpcode = 0x1BF;
    public const ushort DeleteOpcode = 0x1C0;
    public const uint AppendIndex = uint.MaxValue;
    public static GoldScriptDefinitionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Payload.AsSpan();
        if (message.Opcode == UpdateOpcode)
        {
            if (bytes.Length < 4 + SacredDefPosPositionLayout.SerializedSize)
                throw new InvalidDataException("Gold definition update requires an index and a 100-byte definition.");
            return new GoldScriptDefinitionUpdateEvent(message with { Payload = bytes.ToArray() },
                BinaryPrimitives.ReadUInt32LittleEndian(bytes), SacredScriptDefinition.FromPayload(bytes.Slice(4, 100)));
        }
        if (message.Opcode != DeleteOpcode || bytes.Length < 5)
            throw new InvalidDataException("Gold definition deletion requires a kind and a terminated name.");
        var end = bytes[4..].IndexOf((byte)0);
        if (end < 0) throw new InvalidDataException("Unterminated Gold definition deletion name.");
        return new GoldScriptDefinitionDeleteEvent(message with { Payload = bytes.ToArray() },
            BinaryPrimitives.ReadUInt32LittleEndian(bytes), Encoding.Latin1.GetString(bytes.Slice(4, end)));
    }
}
