namespace Sacred.Network.Gameplay;

/// <summary>Gold team membership/leader words and invitation masks. No team gameplay is simulated.</summary>
public sealed class GoldTeamReplica
{
    private readonly uint[] _slots = new uint[GoldTeamCodec.PlayerCount];
    public GoldTeamReplica() => Slots = Array.AsReadOnly(_slots);
    public IReadOnlyList<uint> Slots { get; }
    public byte LocalPlayerSlot { get; private set; }
    public uint IncomingInvitations { get; private set; }
    public uint OutgoingInvitations { get; private set; }
    public bool HasSnapshot { get; private set; }
    public long NotificationCount { get; private set; }
    public uint GetTeam(uint player) => ValidPlayer(player) ? _slots[player - 1] & GoldTeamCodec.TeamMask : 0;
    public bool IsLeader(uint player) => ValidPlayer(player) && (_slots[player - 1] & GoldTeamCodec.LeaderFlag) != 0;
    internal void SetLocalPlayer(byte player) => LocalPlayerSlot = player;

    internal void Apply(GoldTeamEvent entry)
    {
        // Retained bytes and header origin win over metadata supplied to public event constructors.
        switch (GoldTeamCodec.Decode(entry.Message))
        {
            case GoldTeamSnapshotEvent snapshot:
                for (var i = 0; i < _slots.Length; i++) _slots[i] = snapshot.Slots[i];
                IncomingInvitations = snapshot.IncomingInvitations;
                OutgoingInvitations = snapshot.OutgoingInvitations;
                HasSnapshot = true;
                break;
            case GoldTeamInviteEvent invite:
                var origin = invite.Message.Origin;
                if (!ValidPlayer(origin) || !ValidPlayer(invite.Player)) break;
                if (origin == LocalPlayerSlot) OutgoingInvitations |= Bit(invite.Player);
                if (invite.Player == LocalPlayerSlot) IncomingInvitations |= Bit(origin);
                break;
            case GoldTeamJoinEvent join:
                SetTeam(join.Message.Origin, join.Team);
                ClearInvitations(join.Message.Origin);
                break;
            case GoldTeamLeaveEvent leave:
                SetTeam(leave.Player, 0);
                ClearInvitations(leave.Player);
                break;
        }
        NotificationCount++;
    }

    private void SetTeam(uint player, uint rawTeam)
    {
        var team = rawTeam & GoldTeamCodec.TeamMask;
        if (!ValidPlayer(player) || team > GoldTeamCodec.PlayerCount) return;
        var members = team == 0 ? 0 : _slots.Count(value => (value & GoldTeamCodec.TeamMask) == team);
        // Native counts the joining player too; even an existing membership can hit this gate.
        if (members >= 8) return;
        if (team != 0)
        {
            _slots[player - 1] = team | (members == 0 ? GoldTeamCodec.LeaderFlag : 0);
            return;
        }
        if (IsLeader(player))
        {
            var oldTeam = GetTeam(player);
            if (ValidPlayer(oldTeam))
            {
                var replacement = Array.FindIndex(_slots, value => (value & GoldTeamCodec.TeamMask) == oldTeam);
                if (replacement == player - 1)
                    replacement = Array.FindIndex(_slots, replacement + 1, value => (value & GoldTeamCodec.TeamMask) == oldTeam);
                if (replacement >= 0) _slots[replacement] |= GoldTeamCodec.LeaderFlag;
            }
        }
        _slots[player - 1] = 0;
    }

    private void ClearInvitations(uint player)
    {
        if (!ValidPlayer(player)) return;
        var mask = ~Bit(player);
        IncomingInvitations &= mask;
        OutgoingInvitations = player == LocalPlayerSlot ? 0 : OutgoingInvitations & mask;
    }
    private static bool ValidPlayer(uint player) => player is >= 1 and <= GoldTeamCodec.PlayerCount;
    private static uint Bit(uint player) => 1u << (int)(player - 1);
    internal void Clear()
    {
        Array.Clear(_slots); LocalPlayerSlot = 0; IncomingInvitations = OutgoingInvitations = 0;
        HasSnapshot = false; NotificationCount = 0;
    }
}
