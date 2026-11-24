using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameServer.Wire;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Auth.Hero;

/// <summary>
/// Writes a player's hero file from the hero the lobby delivered (the baseline) and what the server knows now
/// (<see cref="LiveHero"/>). Only what the server can see is replaced; everything else stays byte for byte:
/// <list type="bullet">
/// <item>C7 hero: equipment references, creature values (hit points block, the +0x08 value block, combat arts (22-byte
/// records), <b>combos</b> (38-byte records, copied verbatim and in order) and the 9-byte record list. Status modifiers
/// (28-byte records), the 68-byte list, the hero attachment and the creature prefix stay as saved: the wire never carries
/// them.</item>
/// <item>CA/CB inventories: the latest 0x122 streams with references mapped back to file references; the inventory id and
/// owner word keep their saved values.</item>
/// <item>C8 objects: one record per item the inventories and the equipment refer to, built from the server's item states
/// (<see cref="SavedObjects.WriteItem"/>, including all <b>socket</b> records), followed by the saved creature records
/// (companions, horse) and items attached to the hero model unchanged.</item>
/// <item>C3 profile: the level word. CE: the statistics extension from 0x14F.</item>
/// </list>
/// The quest pairs are part of the inventory streams (kept current from 0x145, <see cref="QuestRecords"/>). Chunk CD is the
/// explored map (<see cref="ExploredMap"/>), client-only like the start info (C4): both stay as delivered.
/// When nothing changed, the decoded chunks are identical to the baseline. Runs on the game loop thread; buffers are
/// allocated once per writer.
/// </summary>
public sealed class HeroWriter
{
    public const int MaxFileBytes = 1 << 20;
    private const int HeroPrefixBytes = SacredSavedHero.PrefixSize, CreatureValuesOffset = SacredSavedCreatureLayout.ValuesOffset;
    private readonly ZlibInflater _inflater = new();
    private readonly ZlibDeflater _deflater = new();
    private readonly byte[] _file = new byte[MaxFileBytes], _output = new byte[MaxFileBytes];
    private readonly byte[] _baseHero = new byte[64 * 1024], _hero = new byte[64 * 1024];
    private readonly byte[] _baseObjects = new byte[MaxFileBytes], _objects = new byte[MaxFileBytes];
    private readonly byte[] _inventory = new byte[64 * 1024], _shared = new byte[64 * 1024], _profile = new byte[HeroFile.PreviewBytes];
    private readonly Dictionary<uint, uint> _toFile = new(256);
    private readonly HashSet<uint> _reserved = new(64), _written = new(256), _baseReferenced = new(256);
    private readonly List<uint> _items = new(256);
    private readonly List<(int Offset, int Length)> _baseItems = new(256), _baseCreatures = new(16);
    private readonly bool[] _templateUsed = new bool[4096];
    private uint _nextFree;

    /// <summary>Why the last <see cref="Write"/> returned -1 (diagnostics).</summary>
    public string? Error { get; private set; }
    /// <summary>Item references the inventories or equipment named but the server had no item state for (dropped from the save).</summary>
    public int MissingItems { get; private set; }

    /// <summary>The content (130-byte item data plus records, lower-case hex) of every saved item of a lobby block, counted: what the hero
    /// already had when it joined (the strict item gate lets the client announce exactly these).</summary>
    public bool BaselineItemContents(ReadOnlySpan<byte> baselineBlock, Dictionary<string, int> into)
    {
        var fileLength = HeroFile.ReadBlock(baselineBlock, _inflater, _file);
        if (fileLength < 0) return false;
        var baseline = _file.AsSpan(0, fileLength);
        if (HeroFile.ChunkCount(baseline) < 0) return false;
        var version = baseline[3];
        var objectsIndex = HeroFile.FindChunk(baseline, HeroFile.Objects, out _, out _);
        if (objectsIndex < 0) return false;
        var length = HeroFile.Decode(baseline, objectsIndex, _inflater, _baseObjects);
        if (length < 4) return false;
        var list = _baseObjects.AsSpan(0, length);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(list);
        var position = 4;
        for (var i = 0; i < count; i++)
        {
            if (position + 4 > list.Length) return false;
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(list[position..]);
            position += 4;
            if (resource == 0) continue;
            var size = SavedObjects.RecordSize(list[position..], version);
            if (size < 0) return false;
            var data = list[position..];
            if (SavedObjects.IsItem(data))
            {
                var key = Convert.ToHexStringLower(SavedObjects.SavedContent(data));
                into[key] = into.GetValueOrDefault(key) + 1;
            }
            position += size;
        }
        return true;
    }

    /// <summary>Writes the new lobby block into <paramref name="block"/>; returns its length or -1 (see <see cref="Error"/>).</summary>
    public int WriteBlock(ReadOnlySpan<byte> baselineBlock, LiveHero live, Span<byte> block)
    {
        var fileLength = HeroFile.ReadBlock(baselineBlock, _inflater, _file);
        if (fileLength < 0) return Fail("the delivered block does not inflate");
        var length = Write(_file.AsSpan(0, fileLength), live, _output);
        if (length < 0) return -1;
        var written = HeroFile.WriteBlock(_output.AsSpan(0, length), _deflater, block);
        return written < 0 ? Fail("the block does not fit") : written;
    }

    /// <summary>Writes the new hero file into <paramref name="output"/>; returns its length or -1.</summary>
    public int Write(ReadOnlySpan<byte> baseline, LiveHero live, Span<byte> output)
    {
        Error = null; MissingItems = 0;
        if (HeroFile.ChunkCount(baseline) < 0) return Fail("not an AMH hero file");
        var version = baseline[3];
        var heroIndex = HeroFile.FindChunk(baseline, HeroFile.Hero, out _, out _);
        var objectsIndex = HeroFile.FindChunk(baseline, HeroFile.Objects, out _, out _);
        var inventoryIndex = HeroFile.FindChunk(baseline, HeroFile.Inventory, out _, out _);
        var sharedIndex = HeroFile.FindChunk(baseline, HeroFile.SharedInventory, out _, out _);
        var profileIndex = HeroFile.FindChunk(baseline, HeroFile.Profile, out _, out _);
        var statsIndex = HeroFile.FindChunk(baseline, HeroFile.StatisticsExtension, out _, out _);
        if (heroIndex < 0 || objectsIndex < 0 || inventoryIndex < 0 || profileIndex < 0) return Fail("the hero file lacks hero, objects, inventory or profile");
        if (live.HeroBody.IsEmpty && live.HeroFullBody.IsEmpty) return Fail("no hero state from the player yet");
        if (live.InventoryStream.IsEmpty) return Fail("no inventory from the player yet");

        var heroLength = HeroFile.Decode(baseline, heroIndex, _inflater, _baseHero);
        var objectsLength = HeroFile.Decode(baseline, objectsIndex, _inflater, _baseObjects);
        var profileLength = HeroFile.Decode(baseline, profileIndex, _inflater, _profile);
        if (heroLength < HeroPrefixBytes + CreatureValuesOffset || objectsLength < 4 || profileLength != HeroFile.PreviewBytes) return Fail("a baseline chunk does not decode");
        var baseHero = _baseHero.AsSpan(0, heroLength);
        var heroFileReference = SavedObjects.Reference(baseHero[HeroPrefixBytes..]);
        _baseReferenced.Clear();
        for (var i = 0; i < SacredSavedCreatureLayout.EquipmentCount; i++)
            _baseReferenced.Add(BinaryPrimitives.ReadUInt32LittleEndian(baseHero[(HeroPrefixBytes + SacredSavedCreatureLayout.EquipmentOffset + i * 4)..]));
        foreach (var index in (ReadOnlySpan<int>)[inventoryIndex, sharedIndex])
            if (index >= 0) AddReferenced(Saved(baseline, index, _inventory));
        if (!IndexObjects(_baseObjects.AsSpan(0, objectsLength), version, heroFileReference)) return Fail("the saved object list does not parse");

        // References: the hero keeps its saved reference; items keep their session reference unless that collides with a
        // reference the file keeps (hero, saved creatures), then they get a free one.
        _toFile.Clear(); _reserved.Clear(); _items.Clear(); _written.Clear();
        _reserved.Add(heroFileReference);
        _nextFree = Math.Max(heroFileReference, GoldReferenceCodec.FirstObjectReference);
        foreach (var (offset, _) in _baseCreatures)
        {
            var reference = SavedObjects.Reference(_baseObjects.AsSpan(offset));
            _reserved.Add(reference); _nextFree = Math.Max(_nextFree, reference);
        }
        _toFile[live.Slot] = heroFileReference;

        // Inventories first: they name the items that are saved.
        var inventory = MapInventory(live.InventoryStream, Saved(baseline, inventoryIndex, _inventory), _inventory);
        if (inventory < 0) return Fail("the player's inventory does not parse");
        var shared = -1;
        if (sharedIndex >= 0 && !live.SharedInventoryStream.IsEmpty)
        {
            shared = MapInventory(live.SharedInventoryStream, Saved(baseline, sharedIndex, _shared), _shared);
            if (shared < 0) return Fail("the shared inventory does not parse");
        }

        var hero = PatchHero(baseHero, live, version);
        if (hero < 0) return -1;
        var objects = WriteObjects(live, version);
        if (objects < 0) return -1;
        // Equipment or inventory entries whose item state is unknown were cleared; the inventories must not keep them either.
        ClearUnwritten(_inventory.AsSpan(0, inventory));
        if (shared >= 0) ClearUnwritten(_shared.AsSpan(0, shared));
        ClearUnwrittenEquipment(_hero.AsSpan(HeroPrefixBytes));

        var level = BinaryPrimitives.ReadUInt16LittleEndian(_hero.AsSpan(HeroPrefixBytes + CreatureValuesOffset + SacredSavedCreatureValuesLayout.LevelOffset));
        BinaryPrimitives.WriteUInt16LittleEndian(_profile.AsSpan(Sacred.Core.Pax.Hero.SacredHeroProfileLayout.LevelOffset), level);

        Span<HeroFile.Replacement> replacements = new HeroFile.Replacement[6];
        var count = 0;
        replacements[count++] = new(heroIndex, _hero.AsMemory(0, hero));
        replacements[count++] = new(objectsIndex, _objects.AsMemory(0, objects));
        replacements[count++] = new(inventoryIndex, _inventory.AsMemory(0, inventory));
        if (shared >= 0) replacements[count++] = new(sharedIndex, _shared.AsMemory(0, shared));
        replacements[count++] = new(profileIndex, _profile.AsMemory());
        if (statsIndex >= 0 && live.StatisticsExtension is { Length: 100 } stats) replacements[count++] = new(statsIndex, stats);
        var length = HeroFile.Rebuild(baseline, replacements[..count], _deflater, output);
        return length < 0 ? Fail("the rebuilt hero file does not fit") : length;
    }

    private int Fail(string why) { Error = why; return -1; }

    private ReadOnlySpan<byte> Saved(ReadOnlySpan<byte> file, int index, byte[] scratch)
    {
        var length = HeroFile.Decode(file, index, _inflater, scratch);
        return length < 0 ? default : scratch.AsSpan(0, length).ToArray();       // copy: the scratch is overwritten next
    }

    private bool IndexObjects(ReadOnlySpan<byte> list, byte version, uint heroReference)
    {
        _baseItems.Clear(); _baseCreatures.Clear();
        var count = BinaryPrimitives.ReadUInt32LittleEndian(list);
        var position = 4;
        for (var i = 0; i < count; i++)
        {
            if (position + 4 > list.Length) return false;
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(list[position..]);
            position += 4;
            if (resource == 0) continue;
            var size = SavedObjects.RecordSize(list[position..], version);
            if (size < 0) return false;
            // Items attached to the hero itself (base flag 0x400 with the hero as +0x2C parent) are in no inventory or
            // equipment slot and the wire does not carry the parent word: they are kept as saved, like creatures.
            var data = list[position..];
            var attached = (BinaryPrimitives.ReadUInt32LittleEndian(data[18..]) & 0x400) != 0 && BinaryPrimitives.ReadUInt32LittleEndian(data[37..]) == heroReference
                && !_baseReferenced.Contains(SavedObjects.Reference(data));    // equipped items are attached too, but in a slot
            (SavedObjects.IsItem(data) && !attached ? _baseItems : _baseCreatures).Add((position, size));
            position += size;
        }
        if (_baseItems.Count > _templateUsed.Length) return false;
        Array.Clear(_templateUsed, 0, _baseItems.Count);
        return position == list.Length;
    }

    private uint ToFile(uint runtime)
    {
        if (runtime == 0) return 0;
        if (_toFile.TryGetValue(runtime, out var file)) return file;
        file = runtime;
        if (_reserved.Contains(file)) { while (_reserved.Contains(++_nextFree) || _toFile.ContainsValue(_nextFree)) { } file = _nextFree; }
        _toFile[runtime] = file;
        _reserved.Add(file);
        _items.Add(runtime);
        return file;
    }

    private void AddReferenced(ReadOnlySpan<byte> inventory)
    {
        if (InventorySize(inventory) < 0) return;
        for (var cell = 0; cell < SacredSavedInventoryLayout.CellCount; cell++)
            _baseReferenced.Add(BinaryPrimitives.ReadUInt32LittleEndian(inventory[(SacredSavedInventoryLayout.CellsOffset + cell * SacredSavedInventoryLayout.CellSize)..]));
        for (var bar = 0; bar < SacredSavedInventoryLayout.ItemBarCount; bar++)
            _baseReferenced.Add(BinaryPrimitives.ReadUInt32LittleEndian(inventory[(SacredSavedInventoryLayout.ItemBarOffset + bar * SacredSavedInventoryLayout.ItemBarRecordSize)..]));
    }

    // ---- inventories ---------------------------------------------------------------------------------------------------

    private static int InventorySize(ReadOnlySpan<byte> stream)
    {
        if (stream.Length < SacredSavedInventoryLayout.MinimumSize) return -1;
        var quests = BinaryPrimitives.ReadUInt32LittleEndian(stream[SacredSavedInventoryLayout.QuestCountOffset..]);
        var scrolls = (long)SacredSavedInventoryLayout.QuestRecordsOffset + (long)quests * SacredSavedInventoryLayout.QuestRecordSize;
        if (scrolls + 4 > stream.Length) return -1;
        var size = scrolls + 4 + BinaryPrimitives.ReadUInt32LittleEndian(stream[(int)scrolls..]) * (long)SacredSavedInventoryLayout.ScrollRecordSize;
        return size <= stream.Length ? (int)size : -1;
    }

    private int MapInventory(ReadOnlySpan<byte> live, ReadOnlySpan<byte> saved, byte[] target)
    {
        var size = InventorySize(live);
        if (size < 0 || size > target.Length || InventorySize(saved) < 0) return -1;
        var t = target.AsSpan(0, size);
        live[..size].CopyTo(t);
        saved[..2].CopyTo(t);                                                         // inventory id as saved
        saved.Slice(SacredSavedInventoryLayout.OwnerReferenceOffset, 4).CopyTo(t[SacredSavedInventoryLayout.OwnerReferenceOffset..]);
        for (var cell = 0; cell < SacredSavedInventoryLayout.CellCount; cell++)
        {
            var at = t[(SacredSavedInventoryLayout.CellsOffset + cell * SacredSavedInventoryLayout.CellSize)..];
            BinaryPrimitives.WriteUInt32LittleEndian(at, ToFile(BinaryPrimitives.ReadUInt32LittleEndian(at)));
        }
        for (var bar = 0; bar < SacredSavedInventoryLayout.ItemBarCount; bar++)
        {
            var at = t[(SacredSavedInventoryLayout.ItemBarOffset + bar * SacredSavedInventoryLayout.ItemBarRecordSize)..];
            BinaryPrimitives.WriteUInt32LittleEndian(at, ToFile(BinaryPrimitives.ReadUInt32LittleEndian(at)));
        }
        return size;
    }

    private void ClearUnwritten(Span<byte> inventory)
    {
        for (var cell = 0; cell < SacredSavedInventoryLayout.CellCount; cell++)
        {
            var at = inventory[(SacredSavedInventoryLayout.CellsOffset + cell * SacredSavedInventoryLayout.CellSize)..];
            if (!Kept(BinaryPrimitives.ReadUInt32LittleEndian(at))) at[..SacredSavedInventoryLayout.CellSize].Clear();
        }
        for (var bar = 0; bar < SacredSavedInventoryLayout.ItemBarCount; bar++)
        {
            var at = inventory[(SacredSavedInventoryLayout.ItemBarOffset + bar * SacredSavedInventoryLayout.ItemBarRecordSize)..];
            if (!Kept(BinaryPrimitives.ReadUInt32LittleEndian(at))) at[..SacredSavedInventoryLayout.ItemBarRecordSize].Clear();
        }
    }

    private void ClearUnwrittenEquipment(Span<byte> creature)
    {
        for (var i = 0; i < SacredSavedCreatureLayout.EquipmentCount; i++)
        {
            var at = creature[(SacredSavedCreatureLayout.EquipmentOffset + i * 4)..];
            if (!Kept(BinaryPrimitives.ReadUInt32LittleEndian(at))) BinaryPrimitives.WriteUInt32LittleEndian(at, 0);
        }
    }

    /// <summary>A file reference that is in the saved object list (or 0).</summary>
    private bool Kept(uint file) => file == 0 || _written.Contains(file);

    // ---- hero ----------------------------------------------------------------------------------------------------------

    private int PatchHero(ReadOnlySpan<byte> baseHero, LiveHero live, byte version)
    {
        SacredSavedCreature saved;
        try { saved = SacredSavedCreature.FromPayload(baseHero[HeroPrefixBytes..], version); }
        catch (InvalidDataException) { return Fail("the saved hero does not parse"); }
        var compact = live.HeroBody; var full = live.HeroFullBody;
        var newestIsFull = !full.IsEmpty && (live.FullIsNewer || compact.IsEmpty);
        var newest = newestIsFull ? full : compact;
        if (!LiveValues(newest, newestIsFull, out var values)) return Fail("the hero state does not parse");

        var creature = baseHero.Slice(HeroPrefixBytes, saved.SerializedSize);
        var output = _hero.AsSpan();
        baseHero[..HeroPrefixBytes].CopyTo(output);
        var w = HeroPrefixBytes;
        creature[..CreatureValuesOffset].CopyTo(output[w..]);
        for (var i = 0; i < SacredSavedCreatureLayout.EquipmentCount; i++)
        {
            var runtime = live.EquipmentKnown ? live.Equipment[i] : BinaryPrimitives.ReadUInt32LittleEndian(newest[(GoldCreatureStateLayout.EquipmentOffset + i * 4)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(output[(w + SacredSavedCreatureLayout.EquipmentOffset + i * 4)..], ToFile(runtime));
        }
        w += CreatureValuesOffset;

        // Values: marker | 24-byte hit point block | 238-byte block | arts | combos | status modifiers (as saved).
        var savedValues = saved.Values.ToPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(output[w..], GoldCreatureValuesLayout.Marker);
        newest.Slice(values.Start + GoldCreatureValuesLayout.Block128Offset, GoldCreatureValuesLayout.Block128Size).CopyTo(output[(w + 4)..]);
        var block08 = output.Slice(w + SacredSavedCreatureValuesLayout.Block08Offset, SacredSavedCreatureValuesLayout.Block08Size);
        if (!full.IsEmpty && LiveValues(full, true, out var fullValues))
            full.Slice(fullValues.Start + GoldCreatureValuesLayout.FullBlock08Offset, GoldCreatureValuesLayout.FullBlock08Size).CopyTo(block08);
        else savedValues.AsSpan(SacredSavedCreatureValuesLayout.Block08Offset, SacredSavedCreatureValuesLayout.Block08Size).CopyTo(block08);
        if (!values.Full) OverlayCompact(newest, values, block08);
        w += SacredSavedCreatureValuesLayout.Count22Offset;
        w = List(output, w, newest.Slice(values.Records22, values.Count22 * 22), values.Count22, 2);
        w = List(output, w, newest.Slice(values.Records38, values.Count38 * 38), values.Count38, 2);   // combos, verbatim
        var modifiers = saved.Values.Count28;
        var modifiersAt = savedValues.Length - modifiers * 28;
        w = List(output, w, savedValues.AsSpan(modifiersAt, modifiers * 28), modifiers, 2);

        // Records of 9 bytes: u16 count on the wire, u32 in the file.
        w = List(output, w, newest.Slice(values.Records9, values.Count9 * 9), values.Count9, 4);
        // The 68-byte list and the hero attachment are file-only.
        var tail = creature[(CreatureValuesOffset + savedValues.Length + 4 + saved.Count9 * 9)..];
        tail.CopyTo(output[w..]);
        w += tail.Length;
        var remaining = baseHero[(HeroPrefixBytes + saved.SerializedSize)..];
        remaining.CopyTo(output[w..]);
        return w + remaining.Length;
    }

    private static int List(Span<byte> output, int w, ReadOnlySpan<byte> records, int count, int countBytes)
    {
        if (countBytes == 2) BinaryPrimitives.WriteUInt16LittleEndian(output[w..], (ushort)count);
        else BinaryPrimitives.WriteUInt32LittleEndian(output[w..], (uint)count);
        records.CopyTo(output[(w + countBytes)..]);
        return w + countBytes + records.Length;
    }

    private readonly record struct Values(int Start, bool Full, int Records22, int Count22, int Records38, int Count38, int Suffix, int Records9, int Count9);

    /// <summary>Locates the creature values (Gold 0x585C60 layout) and the following 9-byte records in a hero body.</summary>
    private static bool LiveValues(ReadOnlySpan<byte> body, bool full, out Values values)
    {
        values = default;
        var start = GoldCreatureStateLayout.PrefixSize;
        if (body.Length < start + GoldCreatureValuesLayout.CompactMinimumSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[GoldCreatureStateLayout.MarkerOffset..]) != GoldCreatureStateLayout.Marker ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[start..]) != GoldCreatureValuesLayout.Marker) return false;
        var at = start + (full ? GoldCreatureValuesLayout.FullCount22Offset : GoldCreatureValuesLayout.CompactCount22Offset);
        if (at + 2 > body.Length) return false;
        var n22 = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]); var r22 = at + 2; at = r22 + n22 * 22;
        if (at + 2 > body.Length) return false;
        var n38 = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]); var r38 = at + 2; at = r38 + n38 * 38;
        var suffix = at;
        at += full ? GoldCreatureValuesLayout.FullSuffixSize : GoldCreatureValuesLayout.CompactSuffixSize;
        if (at + 2 > body.Length) return false;
        var n9 = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]);
        if (at + 2 + n9 * 9 > body.Length) return false;
        values = new(start, full, r22, n22, r38, n38, suffix, at + 2, n9);
        return true;
    }

    /// <summary>The compact value stream carries these +0x08-block fields (native offset, length); see GoldCreatureValuesWriter.</summary>
    private static void OverlayCompact(ReadOnlySpan<byte> body, in Values v, Span<byte> block08)
    {
        var s = v.Start;
        Copy(body, block08, s + GoldCreatureValuesLayout.CompactWord0COffset, 0x0C, 4);
        Copy(body, block08, s + GoldCreatureValuesLayout.CompactBlock24Offset, 0x24, 8);
        Copy(body, block08, s + GoldCreatureValuesLayout.CompactBlock2COffset, 0x2C, 8);
        Copy(body, block08, s + GoldCreatureValuesLayout.CompactWord46Offset, 0x46, 4);
        Copy(body, block08, s + GoldCreatureValuesLayout.CompactWord56Offset, 0x56, 2);
        Copy(body, block08, v.Suffix, 0x42, 4);
        Copy(body, block08, v.Suffix + 4, 0x1C, 8);
        Copy(body, block08, v.Suffix + 12, 0x58, 1);
    }

    private static void Copy(ReadOnlySpan<byte> body, Span<byte> block08, int from, int native, int length) =>
        body.Slice(from, length).CopyTo(block08[(native - 8)..]);

    // ---- objects -------------------------------------------------------------------------------------------------------

    private int WriteObjects(LiveHero live, byte version)
    {
        var output = _objects.AsSpan();
        var count = 0;
        var w = 4;
        var baseObjects = _baseObjects.AsSpan();
        for (var i = 0; i < _items.Count; i++)
        {
            var runtime = _items[i];
            if (live.Objects is null || !live.Objects(runtime, out var body) || !SavedObjects.ReadLiveItem(body, out var records, out _))
            { MissingItems++; continue; }
            if (records > byte.MaxValue) return Fail($"item {runtime} has {records} value records; the file holds at most 255");
            var template = Template(body, records, baseObjects);
            if (template.IsEmpty) return Fail("no saved item to take the file-only fields from");
            if (w + 4 + SavedObjects.ItemMinimumBytes + records * 130 + SavedObjects.WeaponBytes > output.Length) return Fail("too many objects");
            BinaryPrimitives.WriteUInt32LittleEndian(output[w..], BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.TypeOffset..]));
            var size = SavedObjects.WriteItem(body, _toFile[runtime], template, output[(w + 4)..]);
            if (size < 0) return Fail($"item {runtime} does not convert");
            _written.Add(_toFile[runtime]);
            w += 4 + size; count++;
        }
        foreach (var (offset, length) in _baseCreatures)
        {
            if (w + 4 + length > output.Length) return Fail("too many objects");
            baseObjects.Slice(offset - 4, 4 + length).CopyTo(output[w..]);
            _written.Add(SavedObjects.Reference(baseObjects[offset..]));
            w += 4 + length; count++;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(output, (uint)count);
        _ = version;
        return w;
    }

    /// <summary>The saved record of the same item (same item data and records, not used yet) or, failing that, any saved item.</summary>
    private ReadOnlySpan<byte> Template(ReadOnlySpan<byte> body, int records, ReadOnlySpan<byte> baseObjects)
    {
        var content = SavedObjects.LiveContent(body, records);
        var fallback = -1;
        for (var i = 0; i < _baseItems.Count; i++)
        {
            var record = baseObjects.Slice(_baseItems[i].Offset, _baseItems[i].Length);
            if (fallback < 0) fallback = i;
            if (_templateUsed[i] || !SavedObjects.SavedContent(record).SequenceEqual(content)) continue;
            _templateUsed[i] = true;
            return record;
        }
        // Same item data with other sockets: still the best source for the file-only fields.
        var itemData = content[(records * 130)..];
        for (var i = 0; i < _baseItems.Count; i++)
        {
            var record = baseObjects.Slice(_baseItems[i].Offset, _baseItems[i].Length);
            var saved = SavedObjects.SavedContent(record);
            if (!_templateUsed[i] && saved[^130..].SequenceEqual(itemData)) { _templateUsed[i] = true; return record; }
        }
        return fallback < 0 ? default : baseObjects.Slice(_baseItems[fallback].Offset, _baseItems[fallback].Length);
    }
}
