using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldScriptFunctionEvent(GoldMessage Message, uint Index, SacredScriptFunctionDefinition Definition) : GoldScriptWorldEvent(Message);

/// <summary>TRIGGER_UPDATE_FUNKS: a vector index followed by the original 84-byte function definition.</summary>
public static class GoldScriptFunctionCodec
{
    public const ushort Opcode = 0x1C4;
    public const int PrefixSize = 4;
    public const int MinimumSize = PrefixSize + SacredScriptFunctionLayout.SerializedSize;
    public static GoldScriptFunctionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < MinimumSize)
            throw new InvalidDataException("Gold function update requires an index and an 84-byte definition.");
        var copy = message with { Payload = message.Payload.ToArray() };
        return new(copy, BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload),
            SacredScriptFunctionDefinition.FromPayload(copy.Payload.AsSpan(PrefixSize, SacredScriptFunctionLayout.SerializedSize)));
    }
}
