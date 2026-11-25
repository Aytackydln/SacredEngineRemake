using System.Buffers.Binary;
using System.Text;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Player roster (opcode 0x1B, <c>FUN_004d3030</c>): 17 records of 834 bytes. Record 0 carries the session name, records
/// 1..16 the connected players. The original rebuilds it whenever a player is admitted (internal 0x101 when no password is
/// set), sends the right password (0x12), reports ready (0x0F), changes its connection speed (0x21) or uploads its own record
/// (client 0x1B, <c>FUN_004d2ee0</c>). A changed roster goes to every player that passed the password check; an unchanged
/// one only to the player that triggered it. The client lists the players of the pre-game lobby from it.
/// </summary>
public sealed partial class GameServerHost
{
    public const int RosterEntryBytes = 0x342, RosterBytes = 17 * RosterEntryBytes;

    // Record offsets (client and server use the same 834-byte shape).
    private const int RosterClass = 0x04, RosterBlock = 0x05, RosterReady = 0x06, RosterHost = 0x07,
        RosterSpeed = 0x0C, RosterVersion = 0x10, RosterIdentity = 0x12, RosterName = 0x16, RosterNameBytes = 0x100,
        RosterProfile = 0x116, RosterProfileBytes = 0x22C;

    // FIRSTCONTACT offsets (client 0x7D4FA0, copied by the admission at 0x4EB80F).
    private const int ContactIdentity = 0, ContactBlock = 6, ContactClass = 7, ContactSpeed = 8, ContactName = 0x8A, ContactProfile = 0x18A;

    private readonly byte[] _roster = new byte[RosterBytes];
    private readonly byte[] _rosterSent = new byte[RosterBytes];
    private uint _rosterServerAddress;

    /// <summary>IPv4 as the original stores it in the roster: a.b.c.d becomes the u32 a*2^24 + b*2^16 + c*2^8 + d (written little endian).</summary>
    private static uint RosterAddress(System.Net.IPEndPoint? endPoint)
    {
        if (endPoint is null) return 0;
        Span<byte> ip = stackalloc byte[16];
        var address = endPoint.Address.IsIPv4MappedToIPv6 ? endPoint.Address.MapToIPv4() : endPoint.Address;
        if (!address.TryWriteBytes(ip, out var written) || written != 4) return 0;
        return (uint)(ip[0] << 24 | ip[1] << 16 | ip[2] << 8 | ip[3]);
    }

    /// <summary>Admission (0x4EB80F): seed the player's record from its FIRSTCONTACT payload.</summary>
    private static void SeedRosterEntry(PlayerSession s, ReadOnlySpan<byte> contact)
    {
        var e = s.RosterEntry.AsSpan();
        e.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(e, RosterAddress(s.Connection.RemoteEndPoint)); // player +4 (run 8: record 0 held the server's LAN IP)
        if (contact.Length < ContactProfile + RosterProfileBytes) return;
        e[RosterClass] = contact[ContactClass];
        e[RosterBlock] = contact[ContactBlock];
        BinaryPrimitives.WriteUInt32LittleEndian(e[RosterSpeed..], contact[ContactSpeed]);
        contact.Slice(ContactIdentity, 4).CopyTo(e[RosterIdentity..]);
        contact.Slice(ContactName, RosterNameBytes).CopyTo(e[RosterName..]);
        contact.Slice(ContactProfile, RosterProfileBytes).CopyTo(e[RosterProfile..]);
    }

    /// <summary>FUN_004d3030(slot): rebuild, then send to everybody if it changed, otherwise only to <paramref name="trigger"/>.</summary>
    private void SendRoster(PlayerSession? trigger)
    {
        var roster = _roster.AsSpan();
        roster.Clear();
        if (_rosterServerAddress == 0 && trigger is not null) _rosterServerAddress = RosterAddress(trigger.Connection.LocalEndPoint);
        BinaryPrimitives.WriteUInt32LittleEndian(roster, _rosterServerAddress);   // [0x6341bc]: 14 13 09 0a = 10.9.19.20
        var name = _config.ServerName.AsSpan();
        Encoding.Unicode.GetBytes(name.Length > RosterNameBytes / 2 - 1 ? name[..(RosterNameBytes / 2 - 1)] : name, roster.Slice(RosterName, RosterNameBytes));
        var any = false;
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if (_slots[slot] is not { } p || p.State < SessionState.SlotAssigned) continue;
            var e = roster.Slice(slot * RosterEntryBytes, RosterEntryBytes);
            p.RosterEntry.CopyTo(e);
            if (e[RosterClass] == 7) e[RosterClass] = 6;                    // 0x4D324A
            e[RosterReady] = (byte)(p.Ready ? 1 : 0);
            e[RosterHost] = (byte)(p.IsHost ? 1 : 0);
            any = true;
        }
        if (!any) return;
        if (!roster.SequenceEqual(_rosterSent))
        {
            roster.CopyTo(_rosterSent);
            for (var slot = 1; slot <= MaxSlots; slot++)
                if (_slots[slot] is { PasswordAccepted: true } p && p.State >= SessionState.SlotAssigned) SendMessage(p, 0x01B, 0, 0, roster);
        }
        else if (trigger is { PasswordAccepted: true }) SendMessage(trigger, 0x01B, 0, 0, roster);
    }

    /// <summary>Client 0x1B (FUN_004d2ee0): the player's own 834-byte record. A lobby identity already in use kicks it (reason 2).</summary>
    private void OnPlayerData(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length != RosterEntryBytes) return;
        var identity = BinaryPrimitives.ReadInt32LittleEndian(m.Payload[RosterIdentity..]);
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (slot != s.Slot && _slots[slot] is { } other && other.State >= SessionState.SlotAssigned && identity != -1 &&
                BinaryPrimitives.ReadInt32LittleEndian(other.RosterEntry.AsSpan(RosterIdentity)) == identity)
            {
                Log.Info($"Player #{s.Slot} uses the identity {identity} of #{slot}, removed.");
                RemovePlayer(s, 2, notifyVictim: true);
                return;
            }
        var e = s.RosterEntry.AsSpan();
        e[RosterClass] = m.Payload[RosterClass];
        e[RosterBlock] = m.Payload[RosterBlock];
        m.Payload.Slice(RosterIdentity, 4).CopyTo(e[RosterIdentity..]);
        m.Payload.Slice(RosterProfile, RosterProfileBytes).CopyTo(e[RosterProfile..]);
        s.CharacterBlock = m.Payload[RosterBlock];
        SendRoster(null);
    }

    /// <summary>0x0F (case 0x4CA1E1): the player is ready; roster to all. (The original also sends the player's object to
    /// other ready players; not modelled here.)</summary>
    private void OnPeerReady(PlayerSession s)
    {
        s.Ready = true;
        SendRoster(null);
    }

    /// <summary>0x21 (case 0x4CAE40): connection speed 0..2 (anything else resets it to 0), then the roster.</summary>
    private void OnConnectionSpeed(PlayerSession s, in GoldMessageView m)
    {
        var value = m.Payload.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) : 0;
        BinaryPrimitives.WriteUInt32LittleEndian(s.RosterEntry.AsSpan(RosterSpeed), value < 3 ? value : 0);
        SendRoster(null);
    }
}
