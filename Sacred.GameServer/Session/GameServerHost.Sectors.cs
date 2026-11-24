using System.Buffers.Binary;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Sector bookkeeping of the original server (<c>FUN_004cf0d0</c>, client 0x148, 62 bytes). Per sector the server keeps the
/// mask of players inside, an "initialized" flag and the owning player (the player that runs the sector's scripts and AI):
/// <list type="bullet">
/// <item>Nine entered sectors at +4 and nine left sectors at +22. The first player to enter an empty sector gets script mode 1
/// (+40), the first player ever also the init flag (+49): "INITSECTOR" / "ENTERSECTOR ... with scripts".</item>
/// <item>The answer is the same 62 bytes with words +0/+2 and the left sectors cleared and the flags filled in.</item>
/// <item>Word +0 names the sector the player now stands in: without an owner it becomes the player's (0x020 to it). Word +2 names
/// the sector it came from: if the player owned it, it gets 0x01F and the ownership moves to the first remaining player if
/// that player stands in it (0x020), otherwise the sector has no owner.</item>
/// <item>Fewer than 500 free references reported at +58 grant another 1000 (0x153).</item>
/// </list>
/// In-game senders also get the world environment 0x00B (case 0x4C7202).
/// </summary>
public sealed partial class GameServerHost
{
    private const int SectorTableSize = 0x1964, SectorEntryBytes = 62;

    private readonly ushort[] _sectorMask = new ushort[SectorTableSize];
    private readonly byte[] _sectorOwner = new byte[SectorTableSize];
    private readonly bool[] _sectorInitialized = new bool[SectorTableSize];
    private readonly byte[] _sectorReply = new byte[SectorEntryBytes];

    private void ResetSectors()
    {
        Array.Clear(_sectorMask); Array.Clear(_sectorOwner); Array.Clear(_sectorInitialized);
    }

    private void OnSectorEntry(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length != SectorEntryBytes) return;
        var bit = Bit(s.Slot);
        var e = _sectorReply.AsSpan();
        m.Payload.CopyTo(e);
        if (BinaryPrimitives.ReadUInt32LittleEndian(e[58..]) < LowReferenceMark) GrantReferences(s, JoinerReferenceGrant);
        var current = BinaryPrimitives.ReadUInt16LittleEndian(e);
        var previous = BinaryPrimitives.ReadUInt16LittleEndian(e[2..]);
        BinaryPrimitives.WriteUInt32LittleEndian(e, 0);
        var order = SortedWorldReferences();
        for (var i = 0; i < 9; i++)
        {
            e[40 + i] = 0; e[49 + i] = 0;
            var entered = BinaryPrimitives.ReadUInt16LittleEndian(e[(4 + i * 2)..]);
            if (entered != 0 && entered < SectorTableSize)
            {
                var before = _sectorMask[entered];
                _sectorMask[entered] |= bit;
                if (before == 0)
                {
                    e[40 + i] = 1;
                    if (!_sectorInitialized[entered])
                    {
                        _sectorInitialized[entered] = true; e[49 + i] = 1;
                        Log.Info($"Player {s.Slot}: INITSECTOR {entered} with scripts");
                    }
                    Log.Info($"Player {s.Slot}: ENTERSECTOR {entered} with scripts");
                }
                else Log.Info($"Player {s.Slot}: enters sector {entered} without scripts (Bitmask={before})");
                StreamSectorTo(s, entered, order);                    // 0x4CF281 -> FUN_004cd4e0, before the answer
            }
            var left = BinaryPrimitives.ReadUInt16LittleEndian(e[(22 + i * 2)..]);
            if (left != 0)
            {
                if (left < SectorTableSize) { _sectorMask[left] &= (ushort)~bit; Log.Info($"Player {s.Slot}: EXITSECTOR {left}"); }
                BinaryPrimitives.WriteUInt16LittleEndian(e[(22 + i * 2)..], 0);
            }
        }
        SendMessage(s, 0x148, 0, 0, e);
        if (current != 0 && current < SectorTableSize)
        {
            var changed = s.CurrentSector != current;
            s.CurrentSector = current;
            if (changed && _scripts is not null) ScriptSectorChanged(s);
            if (_sectorOwner[current] == 0) { _sectorOwner[current] = s.Slot; SendMessage(s, 0x020, 0, 0, default); }
            else if (previous != 0 && previous < SectorTableSize && _sectorOwner[previous] == s.Slot) SendMessage(s, 0x01F, 0, 0, default);
        }
        if (previous != 0 && previous < SectorTableSize && _sectorOwner[previous] == s.Slot) HandOverSector(previous, bit);
        if (s.InGame) SendEnvironment(s);
    }

    /// <summary>
    /// FUN_004cd4e0(sector, slot): every object standing in an entered sector goes to the player, in reference order, through
    /// <c>FUN_004cda80</c>: as 0x169 when the player already has it, else as 0x16F (0x16E without a position). The player's
    /// own hero (reference = slot) and objects whose owner is not connected are skipped. Items carried by creatures (+0x1A4)
    /// are not modelled: the mirror has no carrier relation.
    /// </summary>
    private void StreamSectorTo(PlayerSession s, ushort sector, ReadOnlySpan<uint> order)
    {
        var bit = Bit(s.Slot);
        foreach (var reference in order)
        {
            if (reference == s.Slot || !_world.TryGet(reference, out var entry)) continue;
            if (ObjectMirror.Sector(entry) != sector || entry.Owner is 0 or > MaxSlots || _slots[entry.Owner] is null) continue;
            if (entry.LongPrefix) { if ((entry.KnownBy & bit) != 0) SendMessage(s, 0x169, 0, 0, entry.Payload); else SendObject(s, entry); }
            else { SendMessage(s, (entry.KnownBy & bit) != 0 ? (ushort)0x169 : (ushort)0x16F, 0, 0, PositionedState(entry)); entry.KnownBy |= bit; }
        }
    }

    /// <summary>
    /// A 0x16E state in the 24-byte form <c>FUN_004cda80</c> sends: [ref][type][x][y][0][sector u16][0] followed by the object
    /// data. The server re-serializes its object, which in run 8 differed from the upload only in the flags word
    /// (0x00000201 -> 0x00400001): bit 0x200 cleared, bit 0x400000 set; that observed change is applied here.
    /// </summary>
    private ReadOnlySpan<byte> PositionedState(ObjectMirror.Entry entry)
    {
        var p = entry.Payload;
        var d = _streamPayload.AsSpan(0, 24 + p.Length - 8);
        p[..8].CopyTo(d);
        BinaryPrimitives.WriteUInt32LittleEndian(d[8..], BinaryPrimitives.ReadUInt32LittleEndian(p[ObjectMirror.ShortX..]));
        BinaryPrimitives.WriteUInt32LittleEndian(d[12..], BinaryPrimitives.ReadUInt32LittleEndian(p[ObjectMirror.ShortY..]));
        BinaryPrimitives.WriteUInt32LittleEndian(d[16..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(d[20..], BinaryPrimitives.ReadUInt16LittleEndian(p[ObjectMirror.ShortSector..]));
        p[8..].CopyTo(d[24..]);
        var flagsAt = 24 + ObjectMirror.ShortFlags - 8;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(d[flagsAt..]);
        BinaryPrimitives.WriteUInt32LittleEndian(d[flagsAt..], (flags & ~0x200u) | 0x400000u);
        return d;
    }

    /// <summary>The owner left <paramref name="sector"/>: the first remaining player takes it if it stands in it (0x020).</summary>
    private void HandOverSector(ushort sector, ushort leaverBit)
    {
        var remaining = (ushort)(_sectorMask[sector] & ~leaverBit);
        _sectorOwner[sector] = 0;
        if (remaining == 0) return;
        var slot = System.Numerics.BitOperations.TrailingZeroCount(remaining) + 1;
        if (_slots[slot] is { } next && next.CurrentSector == sector)
        {
            _sectorOwner[sector] = (byte)slot;
            SendMessage(next, 0x020, 0, 0, default);
        }
    }

    /// <summary>Client 0x14C (case 0x4C70D7, 12 bytes): forwarded unchanged to every other player whose hero stands in the
    /// sender's sector (<c>FUN_00495f60</c> compares the heroes' sector words). The original also hands the referenced object
    /// to its world first (not modelled).</summary>
    private void OnSectorMessage(PlayerSession s, in GoldMessageView m)
    {
        if (s.CurrentSector == 0) return;
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } other && other != s && other.CurrentSector == s.CurrentSector)
                SendMessage(other, m.Opcode, m.Origin == 0 ? s.Slot : m.Origin, 0, m.Payload);
    }

    /// <summary>A removed player leaves all sectors; sectors it owned are handed over as on a sector change.</summary>
    private void LeaveAllSectors(byte slot)
    {
        var bit = Bit(slot);
        for (var sector = 1; sector < SectorTableSize; sector++)
        {
            if ((_sectorMask[sector] & bit) == 0 && _sectorOwner[sector] != slot) continue;
            if (_sectorOwner[sector] == slot) HandOverSector((ushort)sector, bit);
            _sectorMask[sector] &= (ushort)~bit;
        }
    }
}
