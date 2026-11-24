using System.Buffers.Binary;
using Sacred.GameServer.Auth.Hero;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Auth;

/// <summary>
/// Plausibility of item states a client reports (0x169/0x16B/0x16C/0x16E/0x16F with an item body). Only invariants that every
/// item of the real saves we checked keeps (1845 items in 40 hero files, sockets included) are tested, so a legitimate item is
/// never refused:
/// <list type="bullet">
/// <item>structure: the value records are the socketed runes/gems followed by the item's own 130-byte block (the last one);
/// there are at most as many socket records as the item's own block has open sockets (slot type bytes 1..4, Gold 0x5BE9E0);</item>
/// <item>with the game's item tables (<c>--game-dir</c>, <see cref="ItemFootprints"/>): the object type is an item type
/// (descriptor category set), the item's own value block names the object type or its Weapon.pak base type, and every socket
/// record names an item type.</item>
/// </list>
/// Without the tables only the structure is checked.
/// </summary>
public static class ItemGuard
{
    public const int MaxSockets = 8;
    private const int RecordBytes = SavedObjects.RecordBytes, SlotTypesOffset = 12;

    /// <summary>Checks a live object body (from its base block on). Returns null when it is not an item or a plausible one,
    /// else the reason it is impossible.</summary>
    public static string? Check(ReadOnlySpan<byte> body, ItemFootprints? tables)
    {
        if (!SavedObjects.ReadLiveItem(body, out var records, out _)) return null;
        if (records > MaxSockets) return $"item with {records} socket records";
        var content = SavedObjects.LiveContent(body, records);
        var own = content.Slice(records * RecordBytes, RecordBytes);
        var open = 0;
        for (var i = 0; i < MaxSockets; i++) if (own[SlotTypesOffset + i] is >= 1 and <= 4) open++;
        if (records > open) return $"item with {records} socket records but {open} sockets";
        if (tables is null) return null;
        var type = BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.TypeOffset..]);
        if (tables.Category(type) == 0) return $"object type 0x{type:X} is not an item type";
        var valueType = BinaryPrimitives.ReadUInt32LittleEndian(own);
        if (valueType != type && (valueType == 0 || valueType != tables.Base(type)))
            return $"item 0x{type:X} carries the values of type 0x{valueType:X}";
        for (var i = 0; i < records; i++)
        {
            var socket = BinaryPrimitives.ReadUInt32LittleEndian(content[(i * RecordBytes)..]);
            if (tables.Category(socket) == 0) return $"item 0x{type:X} has socket record {i} of non-item type 0x{socket:X}";
        }
        return null;
    }
}
