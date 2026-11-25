using System.Buffers.Binary;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Wire;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Session;

/// <summary>
/// Authoritative mode: the server's view of each Closed Net hero, kept from the player's own messages and written back to
/// the lobby by <see cref="HeroWriter"/> (anti-dupe saves, see GameServerHost.Authoritative.cs).
/// <list type="bullet">
/// <item>Hero object states 0x169/0x16C/0x16E/0x16F (compact values) and 0x16B (full values) whose reference is the player's slot.</item>
/// <item>0x11E/0x11D equipment assignment/removal on the hero ([creature ref][slot][item ref], Sacred.Network layouts).</item>
/// <item>0x122 inventories [u32 inventory id][stream] for the hero (id = slot) and the shared stash (id 17), as GameServer.exe
/// case 0x4C5D3E accepts them; 0x13E pick / 0x13F place [item ref][cell][inventory id] (FUN_004d3740 / FUN_004d37f0) are
/// applied to the latest stream. A cell holds only the item's anchor: word +4 is 01 | width &lt;&lt; 8 | height &lt;&lt; 16,
/// word +8 the cached item type. The footprint of an item the server never saw in an inventory comes from the game's item
/// tables (<see cref="ItemFootprints"/>, loaded from --game-dir); without them it is written as 1x1 and the client's loader
/// re-reads the item.</item>
/// <item>0x14F statistics: the 100 bytes after the 0x2670-byte primary block are chunk CE.</item>
/// <item>Items come from the server's object world and must be owned by the player; anything else in the player's inventory
/// is not saved.</item>
/// </list>
/// </summary>
public sealed partial class GameServerHost
{
    private const int StatisticsPrimaryBytes = 0x2670, StatisticsExtensionBytes = 100, SharedInventoryId = 17;
    private readonly LiveHero?[] _live = new LiveHero?[MaxSlots + 1];
    private readonly byte[]?[] _heroBaseline = new byte[]?[MaxSlots + 1];
    private readonly Dictionary<uint, (uint Word04, uint Word08)>?[] _footprints = new Dictionary<uint, (uint, uint)>?[MaxSlots + 1];
    private HeroWriter? _heroWriter;

    /// <summary>Item footprints from the game's tables (set once at startup); null writes unseen items as 1x1.</summary>
    public ItemFootprints? ItemFootprints { get; set; }

    /// <summary>Cells written with a guessed 1x1 footprint (diagnostics).</summary>
    public int GuessedFootprints { get; private set; }

    private LiveHero Live(PlayerSession s)
    {
        if (_live[s.Slot] is { } live) return live;
        var slot = s.Slot;
        live = new LiveHero { Slot = slot };
        live.Objects = (uint reference, out ReadOnlySpan<byte> body) =>
        {
            body = default;
            if (!_world.TryGet(reference, out var entry) || entry.Owner != slot) return false;
            var payload = entry.Payload;
            var prefix = entry.LongPrefix ? 24 : 8;
            if (payload.Length <= prefix) return false;
            body = payload[prefix..];
            return true;
        };
        return _live[slot] = live;
    }

    /// <summary>Called with every message of an authoritative session before routing.</summary>
    private void CaptureHeroState(PlayerSession s, in GoldMessageView m)
    {
        var p = m.Payload;
        switch (m.Opcode)
        {
            case 0x169: case 0x16B: case 0x16C: case 0x16E: case 0x16F:
            {
                var prefix = m.Opcode is 0x16C or 0x16E ? 8 : 24;
                if (p.Length <= prefix || (BinaryPrimitives.ReadUInt32LittleEndian(p) & 0x7FFFFFFF) != s.Slot) return;
                var live = Live(s);
                if (m.Opcode == 0x16B) { LiveHero.Store(ref live.FullBody, ref live.FullBodyLength, p[prefix..]); live.FullIsNewer = true; }
                else { LiveHero.Store(ref live.Body, ref live.BodyLength, p[prefix..]); live.FullIsNewer = false; }
                live.EquipmentKnown = false;                                 // the new state carries the equipment
                WitnessHeroGold(s, live, m.Opcode == 0x16B);                 // gold ledger (GameServerHost.Witness.cs)
                return;
            }
            case 0x11B:
            {
                // The hero's own values update (found with a real client): [u32 slot][u32][0xC0DEF00D][the compact values, 0xDEADBEEF first],
                // sent about a second after a gold change (Gear's set_gold / add_gold, a merchant) and then every 30 s. It is the only message that
                // tells the server of a gold change in a stock client's hero; the state messages above come at world load only. Only the gold
                // is taken over, into the stored compact and full state (the saved hero and server.hero(player).gold follow).
                var values = GoldCreatureStateLayout.PrefixSize;
                if (p.Length < 12 + GoldCreatureValuesLayout.CompactMinimumSize || BinaryPrimitives.ReadUInt32LittleEndian(p) != s.Slot ||
                    BinaryPrimitives.ReadUInt32LittleEndian(p[8..]) != GoldCreatureStateLayout.Marker ||
                    BinaryPrimitives.ReadUInt32LittleEndian(p[12..]) != GoldCreatureValuesLayout.Marker) return;
                var live = Live(s);
                var gold = WitnessGold(s, BinaryPrimitives.ReadUInt32LittleEndian(p[(12 + GoldCreatureValuesLayout.CompactWord46Offset)..]));   // gold ledger (GameServerHost.Witness.cs)
                var compactAt = values + GoldCreatureValuesLayout.CompactWord46Offset;
                if (live.BodyLength >= compactAt + 4) BinaryPrimitives.WriteUInt32LittleEndian(live.Body.AsSpan(compactAt), gold);
                var fullAt = values + GoldCreatureValuesLayout.FullBlock08Offset + 62;
                if (live.FullBodyLength >= values + GoldCreatureValuesLayout.FullMinimumSize) BinaryPrimitives.WriteUInt32LittleEndian(live.FullBody.AsSpan(fullAt), gold);
                return;
            }
            case 0x11D: case 0x11E:
            {
                if (p.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(p) != s.Slot) return;
                var live = Live(s);
                var slot = BinaryPrimitives.ReadUInt32LittleEndian(p[4..]);
                var item = BinaryPrimitives.ReadUInt32LittleEndian(p[8..]);
                if (slot >= LiveHero.EquipmentSlots || !live.LoadEquipment()) return;
                if (m.Opcode == 0x11E) live.Equipment[slot] = item;
                else if (live.Equipment[slot] == item) live.Equipment[slot] = 0;      // conditional removal (0x4C55BD)
                return;
            }
            case 0x122:
            {
                if (p.Length < 4) return;
                var id = BinaryPrimitives.ReadUInt32LittleEndian(p);
                var live = Live(s);
                if (id == s.Slot) LiveHero.Store(ref live.Inventory, ref live.InventoryLength, p[4..]);
                else if (id == SharedInventoryId) LiveHero.Store(ref live.SharedInventory, ref live.SharedInventoryLength, p[4..]);
                else return;
                RememberFootprints(s, p[4..]);
                return;
            }
            case 0x13E: case 0x13F:
            {
                if (p.Length < 12) return;
                var reference = BinaryPrimitives.ReadUInt32LittleEndian(p);
                var cell = BinaryPrimitives.ReadUInt32LittleEndian(p[4..]);
                var id = BinaryPrimitives.ReadUInt32LittleEndian(p[8..]);
                var live = Live(s);
                var inventory = id == s.Slot ? live.Inventory.AsSpan(0, live.InventoryLength)
                    : id == SharedInventoryId ? live.SharedInventory.AsSpan(0, live.SharedInventoryLength) : default;
                if (inventory.Length < InventoryCells + InventoryCellCount * InventoryCellBytes || cell >= InventoryCellCount) return;
                if (m.Opcode == 0x13E) PickFromInventory(s, inventory, (int)cell, reference);
                else PlaceInInventory(s, inventory, (int)cell, reference);
                return;
            }
            case 0x14F:
                if (p.Length < StatisticsPrimaryBytes + StatisticsExtensionBytes) return;
                (Live(s).StatisticsExtension ??= new byte[StatisticsExtensionBytes]).AsSpan().Clear();
                p.Slice(StatisticsPrimaryBytes, StatisticsExtensionBytes).CopyTo(Live(s).StatisticsExtension);
                return;
        }
    }

    private const int InventoryCells = Sacred.Core.Pax.Hero.Inventory.SacredSavedInventoryLayout.CellsOffset;
    private const int InventoryCellCount = Sacred.Core.Pax.Hero.Inventory.SacredSavedInventoryLayout.CellCount;
    private const int InventoryCellBytes = Sacred.Core.Pax.Hero.Inventory.SacredSavedInventoryLayout.CellSize;

    private Dictionary<uint, (uint, uint)> Footprints(PlayerSession s) => _footprints[s.Slot] ??= new Dictionary<uint, (uint, uint)>(256);

    private void RememberFootprints(PlayerSession s, ReadOnlySpan<byte> inventory)
    {
        if (inventory.Length < InventoryCells + InventoryCellCount * InventoryCellBytes) return;
        var known = Footprints(s);
        for (var cell = 0; cell < InventoryCellCount; cell++)
        {
            var at = inventory[(InventoryCells + cell * InventoryCellBytes)..];
            var reference = BinaryPrimitives.ReadUInt32LittleEndian(at);
            if (reference != 0) known[reference] = (BinaryPrimitives.ReadUInt32LittleEndian(at[4..]), BinaryPrimitives.ReadUInt32LittleEndian(at[8..]));
        }
    }

    /// <summary>0x13E: the item leaves the inventory (its anchor is cleared wherever it is; the client names the anchor cell).</summary>
    private void PickFromInventory(PlayerSession s, Span<byte> inventory, int cell, uint reference)
    {
        var known = Footprints(s);
        for (var i = 0; i < InventoryCellCount; i++)
        {
            var at = inventory.Slice(InventoryCells + i * InventoryCellBytes, InventoryCellBytes);
            var held = BinaryPrimitives.ReadUInt32LittleEndian(at);
            if (held == 0 || (held != reference && i != cell)) continue;
            known[held] = (BinaryPrimitives.ReadUInt32LittleEndian(at[4..]), BinaryPrimitives.ReadUInt32LittleEndian(at[8..]));
            at.Clear();
        }
    }

    /// <summary>0x13F: the item's anchor goes to <paramref name="cell"/>.</summary>
    private void PlaceInInventory(PlayerSession s, Span<byte> inventory, int cell, uint reference)
    {
        PickFromInventory(s, inventory, -1, reference);                      // an item has one anchor
        var at = inventory.Slice(InventoryCells + cell * InventoryCellBytes, InventoryCellBytes);
        if (!Footprints(s).TryGetValue(reference, out var footprint))
        {
            var type = _world.TryGet(reference, out var entry) && entry.Payload.Length >= 8 ? BinaryPrimitives.ReadUInt32LittleEndian(entry.Payload[4..]) : 0;
            footprint = (ItemFootprints?.AnchorWord(type) ?? 0x00010101u, type & 0xFFFF);
            if (ItemFootprints is null) GuessedFootprints++;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(at, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(at[4..], footprint.Item1);
        BinaryPrimitives.WriteUInt32LittleEndian(at[8..], footprint.Item2);
    }

    /// <summary>The block the lobby delivered becomes the base of this player's saves.</summary>
    private void RememberBaseline(PlayerSession s, byte[] block) { if (s.Slot != 0) _heroBaseline[s.Slot] = block; }

    private void ForgetHeroState(byte slot)
    {
        if (slot == 0 || slot > MaxSlots) return;
        _live[slot] = null; _heroBaseline[slot] = null; _footprints[slot]?.Clear();
    }

    /// <summary>The built-in serializer: baseline + live state. Returns the block length or 0 (reason logged).</summary>
    private int SerializeHero(PlayerSession s, Span<byte> destination)
    {
        if (_heroBaseline[s.Slot] is not { } baseline || _live[s.Slot] is not { } live) return 0;
        var writer = _heroWriter ??= new HeroWriter();
        var length = writer.WriteBlock(baseline, live, destination);
        if (length < 0) { Log.Info($"AUTH: hero of {s.Describe()} not saved: {writer.Error}."); return 0; }
        if (writer.MissingItems > 0) Log.Info($"AUTH: {s.Describe()}: {writer.MissingItems} inventory entries without an item of the player were left out.");
        var block = new byte[length];                                        // the next save starts from this one
        destination[..length].CopyTo(block);
        _heroBaseline[s.Slot] = block;
        return length;
    }
}
