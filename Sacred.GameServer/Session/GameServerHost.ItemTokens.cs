using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Simulation;
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

    /// <summary>An item with sockets is made in two steps: the creation holds fewer value records than the token (the socket entries and the
    /// item's own record change when the runes are put in), a later update completes it. A token of the same type with more records than the
    /// creation covers the creation provisionally; <see cref="PumpTokenPending"/> then checks the finished item against the token's records.</summary>
    private ItemToken? FindTokenPartial(PlayerSession s, uint type, int contentLength)
    {
        var now = _clock.ElapsedMilliseconds;
        foreach (var t in _tokens)
            if (t.Owner == s && t.Account == s.LobbyIdentity && t.Block == s.CharacterBlock && t.ExpiresMs >= now && t.Type == type && t.Records.Length > contentLength) return t;
        return null;
    }

    private sealed record PendingToken(PlayerSession Owner, uint Reference, long TokenId, long DeadlineMs);
    private readonly List<PendingToken> _tokenPending = new();

    /// <summary>PumpAuthoritative: items admitted on a token with more records than their creation had must have the token's records within
    /// <c>AUTH_TOKEN_COMPLETE_MS</c>; otherwise they are logged, and with AUTH_STRICT_ITEMS=on removed from the server's world (never saved).</summary>
    private void PumpTokenPending(long now)
    {
        for (var i = _tokenPending.Count - 1; i >= 0; i--)
        {
            var p = _tokenPending[i];
            var token = _tokens.Find(t => t.Id == p.TokenId);
            if (p.Owner.State == SessionState.Closed || !_world.TryGet(p.Reference, out var entry) || Item(p.Reference) is null) { _tokenPending.RemoveAt(i); continue; }
            var body = entry.Payload[(entry.LongPrefix ? 24 : 8)..];
            if (!SavedObjects.ReadLiveItem(body, out var records, out _)) { _tokenPending.RemoveAt(i); continue; }
            var content = Convert.ToHexStringLower(SavedObjects.LiveContent(body, records));
            if (token is null || content == token.Records) { _tokenPending.RemoveAt(i); continue; }           // complete (or the token was claimed: the hero holds it whole)
            if (now < p.DeadlineMs) continue;
            _tokenPending.RemoveAt(i);
            StrictItemsRefused++;
            Log.Info($"AUTH: strict items: {p.Owner.Describe()}: item 0x{p.Reference:X} was admitted on a token but never became the item of the token (has {content.Length / 260} of {token.Records.Length / 260} records)"
                + (_config.Auth.StrictItems == StrictItemsMode.On ? ", removed." : " (log mode, kept)."));
            if (_config.Auth.StrictItems == StrictItemsMode.On) _world.Remove(p.Reference);
        }
    }

    /// <summary>claim_item for an item that is in the hero (not destroyed): consumes the token minted for it.</summary>
    private bool ClaimArrivedItem(PlayerSession s, uint reference)
    {
        if (HeroItem(s.Slot, reference) is not { } item || item.Where is "gone" or "world" || item.Type == 0) return false;
        if (FindToken(s, item.Type, item.Records) is not { } token) return false;
        _tokens.Remove(token);
        return true;
    }

    /// <summary>The gate: true when the item state is refused (log mode: only counted and logged, so false). Order: a script token, an item of
    /// the hero's baseline block, then the witness (GameServerHost.Witness.cs: trade, rebuilt from destroyed items, loot of a kill, a container's
    /// content, a sector the player took over). Each source can be switched off with AUTH_ITEM_SOURCES.</summary>
    private bool StrictRefuses(PlayerSession s, uint reference, ReadOnlySpan<byte> body, ReadOnlySpan<byte> payload, int prefix, bool purchase = false)
    {
        var mode = _config.Auth.StrictItems;
        if (mode == StrictItemsMode.Off || !HeroSavedByServer(s.Slot) || _world.TryGet(reference, out _)) return false;
        if (!SavedObjects.ReadLiveItem(body, out var records, out _)) return false;
        var type = BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.TypeOffset..]);
        var witness = WitnessConfig;
        var content = Convert.ToHexStringLower(SavedObjects.LiveContent(body, records));
        if (witness.Accepts(ItemSource.Token) && FindToken(s, type, content) is not null) { Witness!.Explained[(int)ItemSource.Token]++; return false; }
        if (witness.Accepts(ItemSource.Token) && FindTokenPartial(s, type, content.Length) is { } partial)
        {
            _tokenPending.Add(new PendingToken(s, reference, partial.Id, _clock.ElapsedMilliseconds + witness.TokenCompleteMs));
            Witness!.Explained[(int)ItemSource.Token]++;
            return false;
        }
        if (witness.Accepts(ItemSource.Baseline))
        {
            if (_baselineItems[s.Slot] is not { } held || !ReferenceEquals(held.Owner, s))
            {
                var counts = new Dictionary<string, int>();
                if (_heroBaseline[s.Slot] is { } block) (_heroWriter ??= new HeroWriter()).BaselineItemContents(block, counts);
                held = (s, counts);
                _baselineItems[s.Slot] = held;
            }
            if (held.Counts.TryGetValue(content, out var left) && left > 0) { held.Counts[content] = left - 1; Witness!.Explained[(int)ItemSource.Baseline]++; return false; }
        }
        var source = ExplainCreation(s, reference, type, payload, prefix, body, records, purchase);
        if (source != ItemSource.Unexplained) return false;
        StrictItemsRefused++;
        Log.Info($"AUTH: strict items: {s.Describe()}: item 0x{reference:X} (type 0x{type:X}, {records} records) created by the client is explained by nothing "
            + $"(no token, baseline item, trade, destroyed item, kill, container, new sector, world start or paid purchase; last messages: {Trail(s)})"
            + (mode == StrictItemsMode.On ? ", refused." : " (log mode, accepted)."));
        if (mode == StrictItemsMode.On && witness.CountViolations) Violation(s, $"item 0x{reference:X} without a source");
        return mode == StrictItemsMode.On;
    }
}
