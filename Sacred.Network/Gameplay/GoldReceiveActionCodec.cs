using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldReceiveActionEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldScriptInvokeEvent(GoldMessage Message, string FunctionName) : GoldReceiveActionEvent(Message);
public abstract record GoldCreatureReceiveActionEvent(GoldMessage Message, uint ObjectReference) : GoldReceiveActionEvent(Message);
public sealed record GoldCreatureMarkerEvent(GoldMessage Message, uint ObjectReference) : GoldCreatureReceiveActionEvent(Message, ObjectReference);
public sealed record GoldCreatureTimedControlEvent(GoldMessage Message, uint ObjectReference, uint ControlWord, uint ClockWord)
    : GoldCreatureReceiveActionEvent(Message, ObjectReference)
{
    public uint NativeEventId => 0x212;
    /// <summary>Native handler resets +208 to interval +204 minus three, independently of either wire operand.</summary>
    public float ResetElapsed(float interval) => interval - 3f;
}
public sealed record GoldCreatureTypeChangeEvent(GoldMessage Message, uint ObjectReference, uint ObjectType)
    : GoldCreatureReceiveActionEvent(Message, ObjectReference);
public sealed record GoldRegionCountdownEvent(GoldMessage Message, uint Remaining, uint SelectorWord) : GoldReceiveActionEvent(Message)
{
    public byte RegionIndex => (byte)SelectorWord;
}

/// <summary>Gold script invocation, creature marker/control/type requests and region countdown receipts.</summary>
public static class GoldReceiveActionCodec
{
    public const ushort ScriptInvokeOpcode = 0x110;
    public const ushort CreatureMarkerOpcode = 0x13D;
    public const ushort TimedControlOpcode = 0x178;
    public const ushort RegionCountdownOpcode = 0x17E;
    public const ushort TypeChangeOpcode = 0x1D2;
    public const uint TimedControlFlags = 0x00060000;

    public static GoldReceiveActionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            ScriptInvokeOpcode => 1, CreatureMarkerOpcode => 4, TimedControlOpcode => 12,
            RegionCountdownOpcode or TypeChangeOpcode => 8,
            _ => throw new InvalidDataException("Not a Gold receive action.")
        };
        if (message.Payload.Length < size) throw new InvalidDataException("Truncated Gold receive action.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        if (message.Opcode == ScriptInvokeOpcode)
        {
            if (!owned.Payload.Contains((byte)0)) throw new InvalidDataException("Unterminated Gold function name.");
            return new GoldScriptInvokeEvent(owned, GoldScriptNames.Read(owned.Payload));
        }
        return message.Opcode switch
        {
            CreatureMarkerOpcode => new GoldCreatureMarkerEvent(owned, Word(0)),
            TimedControlOpcode => new GoldCreatureTimedControlEvent(owned, Word(0), Word(4), Word(8)),
            RegionCountdownOpcode => new GoldRegionCountdownEvent(owned, Word(0), Word(4)),
            _ => new GoldCreatureTypeChangeEvent(owned, Word(0), Word(4))
        };
    }
}
