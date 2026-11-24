using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Auth.Hero;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Session;

/// <summary>
/// One-time item tokens and the strict item gate (<c>--auth-strict-items</c>, Closed Net with the hero kept by the server).
/// A script that hands an item to a player (the shared stash's withdrawal, the auction house's pickups) mints a token with
/// <c>server.item_token</c>: type and value records of the item, the player's session, lobby account and character, an expiry. The
/// client needs to attach nothing: its creation of that item (0x16F / 0x16E with the same type and records) matches the token. The token
/// is consumed by <c>server.claim_item</c> once the item is in the hero. With the gate on, creations by the client that no token covers and that
/// are not items of the hero's baseline block are refused (no state in the server's world, so the hero never saves them).
/// <para><b>Not known yet</b> (so the gate stays opt-in): items created by the client's own game logic that the server cannot verify,
/// namely loot of creatures the client simulates, merchant purchases, crafting and socketing results, quest rewards and items another player
/// hands over in a trade. In <c>log</c> mode they are counted and logged, not refused.</para>
/// </summary>
public sealed partial class GameServerHost
{
    private sealed class ItemToken
    {
        public long Id;
        public required PlayerSession Owner;
        public int Account;
        public ushort Block;
        public uint Type;
        public required string Records;
        public long ExpiresMs;
        public required string Label;
    }

    private readonly List<ItemToken> _tokens = new();
    private long _nextToken = 1;
    private readonly (PlayerSession Owner, Dictionary<string, int> Counts)?[] _baselineItems = new (PlayerSession, Dictionary<string, int>)?[MaxSlots + 1];

    /// <summary>Items the strict gate refused (or, in log mode, would have refused), for diagnostics and tests.</summary>
    public int StrictItemsRefused { get; private set; }

    public long MintItemToken(int slot, uint type, string records, double ttlSeconds, string label)
    {
        if (AuthoritativeSession(slot) is not { } s || !HeroSavedByServer(slot) || type == 0 || records.Length is 0 or > 9 * 260 || records.Any(c => !Uri.IsHexDigit(c))) return 0;
        var now = _clock.ElapsedMilliseconds;
        _tokens.RemoveAll(t => t.ExpiresMs < now || t.Owner.State == SessionState.Closed);
        if (_tokens.Count >= 4096) return 0;
        var token = new ItemToken
        {
            Id = _nextToken++, Owner = s, Account = s.LobbyIdentity, Block = s.CharacterBlock, Type = type, Records = records.ToLowerInvariant(),
            ExpiresMs = now + (long)(Math.Clamp(ttlSeconds, 1, 600) * 1000), Label = label,
        };
        _tokens.Add(token);
        return token.Id;
    }

    public bool RevokeItemToken(long id) => _tokens.RemoveAll(t => t.Id == id) > 0;

    private ItemToken? FindToken(PlayerSession s, uint type, string records)
    {
        var now = _clock.ElapsedMilliseconds;
        foreach (var t in _tokens)
            if (t.Owner == s && t.Account == s.LobbyIdentity && t.Block == s.CharacterBlock && t.ExpiresMs >= now && t.Type == type && t.Records == records) return t;
        return null;
    }

    /// <summary>claim_item for an item that is in the hero (not destroyed): consumes the token minted for it.</summary>
    private bool ClaimArrivedItem(PlayerSession s, uint reference)
    {
        if (HeroItem(s.Slot, reference) is not { } item || item.Where is "gone" or "world" || item.Type == 0) return false;
        if (FindToken(s, item.Type, item.Records) is not { } token) return false;
        _tokens.Remove(token);
        return true;
    }

    /// <summary>The gate: true when the item state is refused (log mode: only counted and logged, so false).</summary>
    private bool StrictRefuses(PlayerSession s, uint reference, ReadOnlySpan<byte> body)
    {
        var mode = _config.Auth.StrictItems;
        if (mode == StrictItemsMode.Off || !HeroSavedByServer(s.Slot) || _world.TryGet(reference, out _)) return false;
        if (!SavedObjects.ReadLiveItem(body, out var records, out _)) return false;
        var type = BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.TypeOffset..]);
        var content = Convert.ToHexStringLower(SavedObjects.LiveContent(body, records));
        if (FindToken(s, type, content) is not null) return false;
        if (_baselineItems[s.Slot] is not { } held || !ReferenceEquals(held.Owner, s))
        {
            var counts = new Dictionary<string, int>();
            if (_heroBaseline[s.Slot] is { } block) (_heroWriter ??= new HeroWriter()).BaselineItemContents(block, counts);
            held = (s, counts);
            _baselineItems[s.Slot] = held;
        }
        if (held.Counts.TryGetValue(content, out var left) && left > 0) { held.Counts[content] = left - 1; return false; }
        StrictItemsRefused++;
        Log.Info($"AUTH: strict items: {s.Describe()}: item 0x{reference:X} (type 0x{type:X}) created by the client (content {content[..16]}..{content.Length}, {_tokens.Count} tokens: {string.Join(",", _tokens.Select(k => k.Type + ":" + k.Records.Length + ":" + k.Records[..16]))}) is covered by no token and no baseline item" + (mode == StrictItemsMode.On ? ", refused." : " (log mode, accepted)."));
        return mode == StrictItemsMode.On;
    }
}
