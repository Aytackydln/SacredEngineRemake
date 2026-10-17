using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Player dialogue action receives, distinct from ambient NPC_IDLE_ANIMATION.
/// Gold 0x7DDC07/0x7DDCAB construct messages 0x20F/0x210; creature branches
/// 0x536BA5/0x536BC5 schedule action family 7, stages 3/6. Full action execution is separate.</summary>
public static class GoldNpcDialogActionLayout
{
    public const ushort BeginOpcode = 0x120;
    public const ushort EndOpcode = 0x121;
    public const int ActorOffset = 0;
    public const int TargetOffset = 4;
    public const int BeginSize = 8;
    public const int EndSize = 4;
}

/// <summary>Decoded action request; receiving it alone does not execute dialogue AI or quest bytecode.</summary>
public sealed record GoldNpcDialogActionEvent(GoldMessage Message, uint ActorReference, uint? TargetReference) : GoldGameEvent(Message)
{
    public bool Begin => Message.Opcode == GoldNpcDialogActionLayout.BeginOpcode;
    public ushort ActionFamily => 7;
    public ushort ActionStage => Begin ? (ushort)3 : (ushort)6;
}

public static class GoldNpcDialogActionCodec
{
    public static GoldNpcDialogActionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            GoldNpcDialogActionLayout.BeginOpcode => GoldNpcDialogActionLayout.BeginSize,
            GoldNpcDialogActionLayout.EndOpcode => GoldNpcDialogActionLayout.EndSize,
            _ => throw new InvalidDataException("Expected a Gold dialogue action opcode.")
        };
        if (message.Payload.Length != size) throw new InvalidDataException("Gold dialogue action has an invalid payload length.");
        var copy = message.Payload.ToArray();
        return new(message with { Payload = copy }, BinaryPrimitives.ReadUInt32LittleEndian(copy),
            size == GoldNpcDialogActionLayout.BeginSize ? BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(4)) : null);
    }
}
