using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldTeamEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldTeamJoinEvent(GoldMessage Message, uint Team) : GoldTeamEvent(Message);
public sealed record GoldTeamInviteEvent(GoldMessage Message, uint Player) : GoldTeamEvent(Message);
public sealed record GoldTeamLeaveEvent(GoldMessage Message, uint Team, uint Player) : GoldTeamEvent(Message);
public sealed record GoldTeamSnapshotEvent(GoldMessage Message, IReadOnlyList<uint> Slots,
    uint IncomingInvitations, uint OutgoingInvitations) : GoldTeamEvent(Message);

/// <summary>Gold team receipt. Player slots are 1..16; invalid action operands remain native no-ops.</summary>
public static class GoldTeamCodec
{
    public const ushort JoinOpcode = 0x138;
    public const ushort InviteOpcode = 0x139;
    public const ushort SnapshotOpcode = 0x13A;
    public const ushort LeaveOpcode = 0x13B;
    public const int PlayerCount = 16;
    public const uint LeaderFlag = 0x80000000;
    public const uint TeamMask = 0x7fffffff;

    /// <summary>Gold 0x60EC72: campaign entry joins team 1 before peer readiness.</summary>
    public static GoldMessage CreateCampaignJoin(byte playerSlot)
    {
        if (playerSlot is < 1 or > PlayerCount) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 1);
        return new(JoinOpcode, playerSlot, playerSlot, 0, payload);
    }

    public static GoldTeamEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            JoinOpcode or InviteOpcode => 4,
            LeaveOpcode => 8,
            SnapshotOpcode => 72,
            _ => throw new InvalidDataException("Not a Gold team packet.")
        };
        if (message.Payload.Length < size) throw new InvalidDataException("Truncated Gold team payload.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        return message.Opcode switch
        {
            JoinOpcode => new GoldTeamJoinEvent(owned, Word(0)),
            InviteOpcode => new GoldTeamInviteEvent(owned, Word(0)),
            LeaveOpcode => new GoldTeamLeaveEvent(owned, Word(0), Word(4)),
            _ => new GoldTeamSnapshotEvent(owned, Array.AsReadOnly(Enumerable.Range(0, PlayerCount).Select(i => Word(i * 4)).ToArray()), Word(68), Word(64))
        };
    }
}
