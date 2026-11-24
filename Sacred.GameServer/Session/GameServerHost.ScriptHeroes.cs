using System.Buffers.Binary;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Session;

/// <summary>
/// Authoritative mode, for scripts that move items and gold between a hero and their own storage (the auction house): the
/// server's view of a hero's items (<see cref="LiveHero"/> inventories and equipment, the object world), the items a player
/// destroyed (0x167 from the owner removes the object from the world; its last value records are kept here, the newest
/// <see cref="GoneItemsKept"/> per player, for <see cref="GoneItemSeconds"/> s) and a hero save the lobby confirmed
/// (<see cref="Lobby.LobbyClient.UploadCharacterAcked"/>). The only hook in existing code is one call in RemoveObject.
/// </summary>
public sealed partial class GameServerHost
{
    private const int GoneItemsKept = 64, GoneItemSeconds = 600;
    private sealed record GoneItem(uint Reference, uint Type, string Records, long AtMs);
    private readonly (PlayerSession Owner, List<GoneItem> Items)?[] _goneItems = new (PlayerSession, List<GoneItem>)?[MaxSlots + 1];

    /// <summary>RemoveObject: the owner removed one of its objects; an item's records are kept for a while.</summary>
    private void RememberRemovedItem(PlayerSession s, uint reference, ObjectMirror.Entry entry)
    {
        if (!_config.Auth.Enabled || s.Slot is 0 or > MaxSlots) return;
        if (!ItemBody(entry, out var body) || !SavedObjects.ReadLiveItem(body, out var records, out _)) return;
        var type = BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.TypeOffset..]);
        var list = GoneList(s);
        list.RemoveAll(g => g.Reference == reference);
        if (list.Count >= GoneItemsKept) list.RemoveAt(0);
        list.Add(new GoneItem(reference, type, Convert.ToHexStringLower(SavedObjects.LiveContent(body, records)), _clock.ElapsedMilliseconds));
    }

    private List<GoneItem> GoneList(PlayerSession s)
    {
        if (_goneItems[s.Slot] is { } held && ReferenceEquals(held.Owner, s)) return held.Items;    // a new player in the slot starts empty
        var list = new List<GoneItem>();
        _goneItems[s.Slot] = (s, list);
        return list;
    }

    private static bool ItemBody(ObjectMirror.Entry entry, out ReadOnlySpan<byte> body)
    {
        var payload = entry.Payload;
        var prefix = entry.LongPrefix ? 24 : 8;
        body = payload.Length > prefix ? payload[prefix..] : default;
        return body.Length > 0;
    }

    private PlayerSession? AuthoritativeSession(int slot) =>
        _config.Auth.Enabled && slot is >= 1 and <= MaxSlots && _slots[slot] is { State: not SessionState.Closed } s ? s : null;

    public bool HeroSavedByServer(int slot) =>
        AuthoritativeSession(slot) is { } s && Lobby is { IsOnline: true } && s.LobbyIdentity is not (0 or -1)
        && s.CharacterBlock is >= 1 and <= 8 && s.CharacterReceived;

    public ScriptHeroItem? HeroItem(int slot, uint reference)
    {
        if (AuthoritativeSession(slot) is not { } s) return null;
        reference &= 0x7FFFFFFF;
        var where = WhereInHero(s, reference);
        if (_world.TryGet(reference, out var entry) && entry.Owner == s.Slot && ItemBody(entry, out var body)
            && SavedObjects.ReadLiveItem(body, out var records, out _))
            return new ScriptHeroItem(reference, BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.TypeOffset..]), where ?? "world",
                Convert.ToHexStringLower(SavedObjects.LiveContent(body, records)));
        if (where is not null) return new ScriptHeroItem(reference, 0, where, "");          // anchored, but the server has no state of it
        foreach (var gone in GoneList(s))
            if (gone.Reference == reference && _clock.ElapsedMilliseconds - gone.AtMs <= GoneItemSeconds * 1000L)
                return new ScriptHeroItem(reference, gone.Type, "gone", gone.Records, (_clock.ElapsedMilliseconds - gone.AtMs) / 1000.0);
        return null;
    }

    public IReadOnlyList<ScriptHeroItem> HeroItems(int slot)
    {
        if (AuthoritativeSession(slot) is not { } s || _live[s.Slot] is not { } live) return [];
        var refs = new List<uint>();
        CollectAnchors(live.InventoryStream, refs);
        CollectAnchors(live.SharedInventoryStream, refs);
        if (live.LoadEquipment()) foreach (var item in live.Equipment) if (item != 0 && !refs.Contains(item & 0x7FFFFFFF)) refs.Add(item & 0x7FFFFFFF);
        var items = new List<ScriptHeroItem>(refs.Count);
        foreach (var reference in refs) if (HeroItem(slot, reference) is { Where: not ("world" or "gone") } item) items.Add(item);
        return items;
    }

    public bool ClaimGoneItem(int slot, uint reference)
    {
        if (AuthoritativeSession(slot) is not { } s) return false;
        reference &= 0x7FFFFFFF;
        return GoneList(s).RemoveAll(g => g.Reference == reference) > 0 || ClaimArrivedItem(s, reference);
    }

    public Task<string?> SaveHeroNow(int slot)
    {
        if (!HeroSavedByServer(slot) || Lobby is not { } lobby) return Task.FromResult<string?>("the server does not keep this hero (Closed Net with --authoritative and the lobby online)");
        var s = _slots[slot]!;
        s.HeroSaveDueMs = 0;
        s.NextPeriodicSaveMs = _clock.ElapsedMilliseconds + _config.Auth.SaveIntervalSeconds * 1000L;
        var buffer = _heroBuffer ??= new byte[Sacred.GameServer.Lobby.LobbyProtocol.MaxBlock];
        var length = HeroSerializer is { } serializer ? serializer.Serialize(s, buffer) : SerializeHero(s, buffer);
        if (length <= 0) return Task.FromResult<string?>("the server could not write the hero");
        HeroSaves++;
        Log.Info($"AUTH: hero of {s.Describe()} saved to the lobby (script, {length} bytes).");
        return lobby.UploadCharacterAcked(s.LobbyIdentity, s.CharacterBlock, buffer.AsSpan(0, length), TimeSpan.FromSeconds(30));
    }

    public double? HeroUploadTime(int slot)
    {
        if (AuthoritativeSession(slot) is not { } s || Lobby?.LastUploadMs(s.LobbyIdentity) is not { } at) return null;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - (Environment.TickCount64 - at) / 1000.0;
    }

    /// <summary>Where the hero holds <paramref name="reference"/> (an anchor cell of its own or the shared inventory, or equipped), or null.</summary>
    private string? WhereInHero(PlayerSession s, uint reference)
    {
        if (_live[s.Slot] is not { } live) return null;
        if (HasAnchor(live.InventoryStream, reference)) return "backpack";
        if (HasAnchor(live.SharedInventoryStream, reference)) return "stash";
        if (live.LoadEquipment()) foreach (var item in live.Equipment) if ((item & 0x7FFFFFFF) == reference) return "equipped";
        return null;
    }

    private static bool HasAnchor(ReadOnlySpan<byte> inventory, uint reference)
    {
        if (inventory.Length < InventoryCells + InventoryCellCount * InventoryCellBytes) return false;
        for (var cell = 0; cell < InventoryCellCount; cell++)
            if ((BinaryPrimitives.ReadUInt32LittleEndian(inventory[(InventoryCells + cell * InventoryCellBytes)..]) & 0x7FFFFFFF) == reference) return true;
        return false;
    }

    private static void CollectAnchors(ReadOnlySpan<byte> inventory, List<uint> refs)
    {
        if (inventory.Length < InventoryCells + InventoryCellCount * InventoryCellBytes) return;
        for (var cell = 0; cell < InventoryCellCount; cell++)
        {
            var reference = BinaryPrimitives.ReadUInt32LittleEndian(inventory[(InventoryCells + cell * InventoryCellBytes)..]) & 0x7FFFFFFF;
            if (reference != 0 && !refs.Contains(reference)) refs.Add(reference);
        }
    }
}
