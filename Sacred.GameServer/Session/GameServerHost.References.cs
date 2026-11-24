using System.Buffers.Binary;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Object reference and trigger id allocation of the original server.
/// <list type="bullet">
/// <item>0x153 (<c>FUN_004d6110</c>): free object references are handed out in blocks, in pool order (the pool starts as
/// 0x11..0x1FFFB). A reference whose owner byte is set, or for which an object exists, is skipped. The admission grants the
/// host 3000 and a joiner 1000 once the host world exists (0x4EBFD0/0x4EC070); the host's 0x18 grants 1000 to every other
/// connected player (0x4CA0DA); a sector entry reporting fewer than 500 left grants 1000 (0x4CF118); a client may ask with
/// 0x153 [count].</item>
/// <item>A leaving player's references go back to the end of the pool (<c>FUN_004ce2d0</c>, internal 0x152).</item>
/// <item>0x154 [last known][count] (case 0x4C4B10): world trigger ids, answered with [first][count] to the sender.</item>
/// </list>
/// All buffers are allocated once; the pool is a ring of <see cref="ReferenceLimit"/> entries.
/// </summary>
public sealed partial class GameServerHost
{
    private const uint FirstReference = 0x11, ReferenceLimit = 0x1FFFC;
    private const int HostReferenceGrant = 3000, JoinerReferenceGrant = 1000, LowReferenceMark = 500;

    private readonly byte[] _referenceOwner = new byte[ReferenceLimit];
    private readonly uint[] _referencePool = new uint[ReferenceLimit];
    private readonly byte[] _referenceReply = new byte[(int)ReferenceLimit * 4];
    private int _poolHead, _poolCount = -1;
    private uint _lastTrigger;

    private void ResetReferences()
    {
        Array.Clear(_referenceOwner);
        _poolHead = 0; _poolCount = 0;
        for (var r = FirstReference; r < ReferenceLimit; r++) _referencePool[_poolCount++] = r;
        _lastTrigger = 0;
    }

    /// <summary>FUN_004d6110(count, slot): sends 0x153 with <paramref name="count"/> free references owned by the player from now on.</summary>
    private void GrantReferences(PlayerSession s, int count)
    {
        if (_poolCount < 0) ResetReferences();
        if (count <= 0 || count >= ReferenceLimit) return;
        var granted = 0;
        var inspected = 0;
        var available = _poolCount;
        while (granted < count && inspected < available)
        {
            var reference = _referencePool[_poolHead];
            _poolHead = (_poolHead + 1) % _referencePool.Length; _poolCount--; inspected++;
            if (_referenceOwner[reference] != 0) continue;
            if (_world.TryGet(reference, out var existing)) { _referenceOwner[reference] = existing.Owner; continue; }
            _referenceOwner[reference] = s.Slot;
            BinaryPrimitives.WriteUInt32LittleEndian(_referenceReply.AsSpan(granted * 4), reference);
            granted++;
        }
        if (granted < count)
        {
            // The original logs "Out of object refs", ends the session and kicks everybody (reason 8). A real server never gets here.
            Log.Info($"ERROR: Out of object refs, need {count - granted} more refs!");
        }
        if (granted == 0) return;
        SendMessage(s, 0x153, 0, 0, _referenceReply.AsSpan(0, granted * 4));
        Log.Info($"Player #{s.Slot} requested {count} free object refs, {granted} refs sent " +
            $"({BinaryPrimitives.ReadUInt32LittleEndian(_referenceReply)}-{BinaryPrimitives.ReadUInt32LittleEndian(_referenceReply.AsSpan((granted - 1) * 4))}).");
    }

    /// <summary>FUN_004ce2d0: all references of a leaving player return to the end of the pool.</summary>
    private void ReleaseReferences(byte slot)
    {
        if (_poolCount < 0) return;
        var released = 0;
        for (var r = FirstReference; r < ReferenceLimit; r++)
        {
            if (_referenceOwner[r] != slot) continue;
            _referenceOwner[r] = 0;
            if (_poolCount < _referencePool.Length)
            {
                _referencePool[(_poolHead + _poolCount) % _referencePool.Length] = r;
                _poolCount++;
            }
            released++;
        }
        if (released > 0) Log.Info($"ReleaseClientRefs: released {released} refs from player #{slot}.");
    }

    /// <summary>Client 0x153 [count] (case 0x4C48B5).</summary>
    private void OnReferenceRequest(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 4) return;
        GrantReferences(s, (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(m.Payload), ReferenceLimit));
    }

    /// <summary>Client 0x154 [last known][count] (case 0x4C4B10): the answer is [last + 1][count]; the last id grows by count.</summary>
    private void OnTriggerRequest(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 8) return;
        var known = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[4..]);
        if (known > _lastTrigger) _lastTrigger = known;
        Span<byte> reply = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(reply, _lastTrigger + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(reply[4..], count);
        _lastTrigger += count;
        SendMessage(s, 0x154, 0, 0, reply);
        Log.Info($"Player #{s.Slot} requested {count} free trigger, new last used trigger is now {_lastTrigger}.");
    }
}
