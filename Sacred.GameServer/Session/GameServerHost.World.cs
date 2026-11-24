using System.Buffers.Binary;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Object world of the dispatcher (GameServer.exe): object states 0x16E/0x16F are absorbed into the server's world
/// (<c>FUN_004ce750</c>) and streamed to every in-game player that does not know the object yet (<c>FUN_004cd040</c>), also
/// to a participant before its 0x017 answer; 0x169/0x16C/0x11A/0x11B are absorbed and forwarded (mode 6); 0x16B/0x16D/0x14B
/// are absorbed only (case 0x4C4D07). Removals 0x166/0x167 from the owner delete the object and go to the players that
/// have it (<c>FUN_004ce3b0</c>); from anybody else they only clear that player's "has it" bit (case 0x4C4D86).
/// The sector visibility test of the streamer is approximated by "in game", as for the forwarder modes.
/// </summary>
public sealed partial class GameServerHost
{
    private readonly ObjectMirror _world = new(200_000);
    private uint[] _streamOrder = new uint[4096];
    private readonly byte[] _streamPayload = new byte[ObjectMirror.MaxObjectBytes + 24];

    public int WorldObjectCount => _world.Count;

    private static ushort Bit(byte slot) => (ushort)(1 << (slot - 1));

    /// <summary>Returns true when the message was fully handled by the world.</summary>
    private bool WorldMessage(PlayerSession s, in GoldMessageView m)
    {
        var owner = m.Origin == 0 ? s.Slot : m.Origin;
        switch (m.Opcode)
        {
            case 0x16E: case 0x16F:
            {
                var reference = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) & 0x7FFFFFFF;
                var isNew = m.Opcode == 0x16F && _scripts is not null && !_world.TryGet(reference, out _);
                if (_world.Store(m.Opcode, owner, m.Payload, Bit(s.Slot)))
                {
                    StreamObject(reference);
                    if (isNew) ScriptItemOnGround(s, reference);
                }
                return true;
            }
            case 0x169: case 0x16C:
                _world.Store(m.Opcode, owner, m.Payload, InGameMask());
                return false;                                   // forwarded by the routing table (mode 6)
            case 0x16B:
                _world.Store(m.Opcode, owner, m.Payload, Bit(s.Slot));
                return true;
            case 0x16D: case 0x14B:
                return true;
            case 0x166: case 0x167:
                RemoveObject(s, m);
                return true;
        }
        return false;
    }

    private ushort InGameMask()
    {
        ushort mask = 0;
        for (byte slot = 1; slot <= MaxSlots; slot++) if (_slots[slot] is { InGame: true }) mask |= Bit(slot);
        return mask;
    }

    private void RemoveObject(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 4) return;
        var reference = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) & 0x7FFFFFFF;
        if (!_world.TryGet(reference, out var entry))
        {
            // Not in the server's world (e.g. created before this server saw it): pass it on like the forwarder would.
            Forward(s, m, RouteMode.ObjectVisible, includeSender: false);
            return;
        }
        if (entry.Owner != s.Slot) { entry.KnownBy &= (ushort)~Bit(s.Slot); return; }
        var knownBy = entry.KnownBy;
        RememberRemovedItem(s, reference, entry);                           // authoritative: items a player destroyed (GameServerHost.ScriptHeroes.cs)
        _world.Remove(m.Payload);
        for (byte slot = 1; slot <= MaxSlots; slot++)
            if (slot != s.Slot && (knownBy & Bit(slot)) != 0 && _slots[slot] is { InGame: true } target)
                SendMessage(target, m.Opcode, 0, 0, m.Payload[..4]);
    }

    /// <summary>FUN_004cd040 for one object: to every in-game player that does not have it.</summary>
    private void StreamObject(uint reference)
    {
        if (!_world.TryGet(reference, out var entry)) return;
        for (byte slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { InGame: true } target && (entry.KnownBy & Bit(slot)) == 0) SendObject(target, entry);
    }

    /// <summary>All mirrored references, sorted (the original walks its object table in reference order). Reuses one buffer.</summary>
    private ReadOnlySpan<uint> SortedWorldReferences()
    {
        if (_world.Count == 0) return default;
        if (_streamOrder.Length < _world.Count) _streamOrder = new uint[_world.Count * 2];
        var order = _streamOrder.AsSpan(0, _world.CopyReferences(_streamOrder));
        order.Sort();
        return order;
    }

    /// <summary>Before a participant's 0x017 answer the original streams every object the participant lacks, by reference.</summary>
    private void StreamWorldTo(PlayerSession s)
    {
        var order = SortedWorldReferences();
        var bit = Bit(s.Slot);
        foreach (var reference in order)
            if (_world.TryGet(reference, out var entry) && (entry.KnownBy & bit) == 0) SendObject(s, entry);
    }

    private void SendObject(PlayerSession target, ObjectMirror.Entry entry)
    {
        var opcode = ObjectMirror.StreamOpcode(entry, out var prefix, out var hasPosition);
        var stored = entry.Payload;
        var payload = _streamPayload.AsSpan();
        int length;
        if (hasPosition || !entry.LongPrefix) { stored.CopyTo(payload); length = stored.Length; }
        else
        {
            stored[..8].CopyTo(payload);                      // [ref][type], position fields dropped (0x16E form)
            stored[prefix..].CopyTo(payload[8..]);
            length = 8 + stored.Length - prefix;
        }
        SendMessage(target, opcode, 0, 0, payload[..length]);
        entry.KnownBy |= Bit(target.Slot);
    }
}
