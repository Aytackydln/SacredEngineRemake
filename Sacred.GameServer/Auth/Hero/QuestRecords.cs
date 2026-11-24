using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.GameServer.Auth.Hero;

/// <summary>
/// The hero's quest pairs: the 8-byte <c>[key][word04]</c> records of the inventory stream (count at +4436, records at +4440,
/// followed by the u32 scroll count and 4-byte scroll records; writer 0x5DDA30). They are the only quest state a hero file
/// carries (chunk CD is the explored map, see <see cref="ExploredMap"/>), and the client changes them during play with 0x145
/// <c>[u32 key][u32 word04]</c>:
/// <list type="bullet">
/// <item>Sacred.exe 0x5D8280 (inventory, key, word04, send): with send set, a player inventory (id &lt; 0x11) and a network
/// game it posts 0x145 with both words, then appends the pair locally. The append is refused when an existing pair has the
/// same key or an existing pair's word04 is zero (0x5D8402..0x5D8411); otherwise the pair goes to the end of the vector.</item>
/// <item>The receiving client (0x7DE5F3) calls 0x5D8280 with send = 0 on its own local player's inventory: every recipient
/// appends the same pair to its own hero by the same rule.</item>
/// </list>
/// GameServer.exe only forwards 0x145 (forwarder mode 5, party). Applying the rule to the server's latest 0x122 stream of the
/// sender and of each recipient reproduces the clients' inventories byte for byte; a newer 0x122 replaces the stream as usual.
/// </summary>
public static class QuestRecords
{
    public const ushort Opcode = 0x145;
    public const int PayloadBytes = 8;

    public enum Result : byte { Appended, Refused, Malformed, NoRoom }

    /// <summary>
    /// Appends <c>[key][word04]</c> to the quest pairs of the inventory stream <paramref name="stream"/>[..<paramref name="length"/>]
    /// in place (later bytes move up by 8), following 0x5D8280. The buffer must have 8 spare bytes after the stream.
    /// </summary>
    public static Result Append(Span<byte> stream, int length, uint key, uint word04)
    {
        if (length < SacredSavedInventoryLayout.MinimumSize || length > stream.Length) return Result.Malformed;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(stream[SacredSavedInventoryLayout.QuestCountOffset..]);
        var end = SacredSavedInventoryLayout.QuestRecordsOffset + (long)count * SacredSavedInventoryLayout.QuestRecordSize;
        if (end + 4 > length) return Result.Malformed;                          // the scroll count must follow
        for (var i = 0; i < count; i++)
        {
            var record = stream[(SacredSavedInventoryLayout.QuestRecordsOffset + i * SacredSavedInventoryLayout.QuestRecordSize)..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) == key || BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) == 0) return Result.Refused;
        }
        if (length + SacredSavedInventoryLayout.QuestRecordSize > stream.Length) return Result.NoRoom;
        var at = (int)end;
        stream[at..length].CopyTo(stream[(at + SacredSavedInventoryLayout.QuestRecordSize)..]);
        BinaryPrimitives.WriteUInt32LittleEndian(stream[at..], key);
        BinaryPrimitives.WriteUInt32LittleEndian(stream[(at + 4)..], word04);
        BinaryPrimitives.WriteUInt32LittleEndian(stream[SacredSavedInventoryLayout.QuestCountOffset..], count + 1);
        return Result.Appended;
    }

    /// <summary>Applies a 0x145 to a live hero's inventory stream, growing its buffer when needed (rare: 1 KB at a time).</summary>
    public static Result Apply(LiveHero live, uint key, uint word04)
    {
        if (live.Inventory is null || live.InventoryLength == 0) return Result.Malformed;
        if (live.Inventory.Length < live.InventoryLength + SacredSavedInventoryLayout.QuestRecordSize)
        {
            var grown = new byte[live.InventoryLength + 1024];
            live.Inventory.AsSpan(0, live.InventoryLength).CopyTo(grown);
            live.Inventory = grown;
        }
        var result = Append(live.Inventory, live.InventoryLength, key, word04);
        if (result == Result.Appended) live.InventoryLength += SacredSavedInventoryLayout.QuestRecordSize;
        return result;
    }
}
