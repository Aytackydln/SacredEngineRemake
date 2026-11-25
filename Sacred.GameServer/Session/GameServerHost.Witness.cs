using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Simulation;
using Sacred.GameServer.Wire;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Session;

/// <summary>
/// Host side of the witness (docs/authoritative-world-plan.md, M0..M2): feeds <see cref="ItemWitness"/>, <see cref="GoldLedger"/>,
/// <see cref="OpcodeTrail"/> and the capture with what players send. Hooks in existing code, each one call: <c>Route</c> (capture),
/// <c>Handle</c> (capture), <c>AuthoritativeFilter</c> (events), <c>RememberRemovedItem</c> (destroyed items, gold piles), <c>CompleteTrade</c>, the two 0x020
/// grants of the sector code, <c>CaptureHeroState</c> (gold) and <c>AuthoritativeLeave</c>; the strict item gate
/// (GameServerHost.ItemTokens.cs) asks <see cref="ExplainCreation"/>.
/// </summary>
public sealed partial class GameServerHost
{
    private ItemWitness? _witness;
    private GoldLedger? _goldLedger;
    private readonly OpcodeTrail _trail = new();
    private CaptureWriter? _capture;
    private bool _captureTried;
    private readonly uint[] _creditedPiles = new uint[256];
    private int _creditedPileNext;

    private WitnessOptions WitnessConfig => _config.Auth.Witness;
    /// <summary>The item witness (diagnostics and tests); null outside authoritative mode.</summary>
    public ItemWitness? Witness => _config.Auth.Enabled ? _witness ??= new ItemWitness(WitnessConfig) : null;
    /// <summary>The gold ledger (diagnostics and tests); null outside authoritative mode.</summary>
    public GoldLedger? Gold => _config.Auth.Enabled ? _goldLedger ??= new GoldLedger(WitnessConfig) : null;
    /// <summary>The capture writer while <c>--capture</c> is on.</summary>
    public CaptureWriter? Capture => _capture;
    /// <summary>Kill reports (0x17D) of creatures that still had hit points in the server's world (0x128 applied); diagnostics for M5.</summary>
    public int KillsAlive { get; private set; }
    /// <summary>Game messages received per opcode since the start (admitted players; the replay report lists them).</summary>
    public readonly long[] ReceivedByOpcode = new long[0x400];
    public long WorldPositions => _world.Positions;
    public long WorldHitPointUpdates => _world.HitPointUpdates;
    /// <summary>Gold increases that were not saved (AUTH_STRICT_GOLD=on).</summary>
    public int GoldClamped { get; private set; }

    private ushort _nextCaptureLink;

    /// <summary>The game's pricing for the payment check (<c>--game-dir</c>, AUTH_PRICE_CHECK); null without it.</summary>
    public ItemPrices? ItemPrices { get; set; }
    /// <summary>Purchase or stock items whose stated price differs from the computed one (diagnostics).</summary>
    public int PriceMismatches { get; private set; }

    /// <summary>An item created in the world start window that is not a baseline item went into the hero's inventory while the window was still open:
    /// a client that makes an item while it builds the world and puts it into its hero. Logged; with AUTH_STRICT_ITEMS=on removed from the server's world.</summary>
    private void WorldStartItemInHero(PlayerSession s, uint reference)
    {
        if (_config.Auth.StrictItems == StrictItemsMode.Off || !HeroSavedByServer(s.Slot)) return;
        StrictItemsRefused++;
        Log.Info($"AUTH: strict items: {s.Describe()}: item 0x{reference:X} was created while the world started and is not an item of the hero's baseline, yet went into the hero's inventory"
            + (_config.Auth.StrictItems == StrictItemsMode.On ? ", removed." : " (log mode, kept)."));
        if (_config.Auth.StrictItems == StrictItemsMode.On) _world.Remove(reference);
    }
    private readonly long[] _goldSeen = new long[MaxSlots + 1];
    private readonly uint[] _unpaid = new uint[ItemWitness.PurchaseRing];

    /// <summary>PumpAuthoritative: purchases (items announced with 0x16D) that the hero gold did not pay for in time: logged, and with
    /// AUTH_STRICT_ITEMS=on removed from the server world, so they are never saved.</summary>
    private void PumpPurchases(long now)
    {
        if (_witness is null || _config.Auth.StrictItems == StrictItemsMode.Off) return;
        for (byte slot = 1; slot <= MaxSlots; slot++)
        {
            if (_slots[slot] is not { InGame: true } s) continue;
            var n = _witness.TakeUnpaid(slot, now, _unpaid);
            for (var i = 0; i < n; i++)
            {
                StrictItemsRefused++;
                Log.Info($"AUTH: strict items: {s.Describe()}: purchase 0x{_unpaid[i]:X} was not paid for by the hero gold in time (last messages: {Trail(s)})"
                    + (_config.Auth.StrictItems == StrictItemsMode.On ? ", removed." : " (log mode, kept)."));
                if (_config.Auth.StrictItems == StrictItemsMode.On) _world.Remove(_unpaid[i]);
            }
        }
    }

    /// <summary>Handle: every frame a client sends goes into the capture (its connection numbered on the first frame).</summary>
    private void CaptureFrame(PlayerSession s, in Net.InboundFrame frame, long now)
    {
        if (_capture is null)
        {
            if (_captureTried) return;
            _captureTried = true;
            _capture = CaptureWriter.Open(_config.CaptureDirectory!, now);
            if (_capture is null) return;
        }
        if (s.CaptureLink == 0) s.CaptureLink = ++_nextCaptureLink == 0 ? ++_nextCaptureLink : _nextCaptureLink;
        _capture.Append(now, s.CaptureLink, frame.Type, frame.Sender, frame.Type == Net.InboundFrame.Disconnected ? default : frame.Span);
    }

    private void PumpCapture(long now) => _capture?.Pump(now);

    private void CloseCapture() { _capture?.Dispose(); }

    /// <summary>AuthoritativeFilter: the events the witness explains creations with.</summary>
    private void WitnessObserve(PlayerSession s, in GoldMessageView m)
    {
        var now = _clock.ElapsedMilliseconds;
        _trail.Add(s.Slot, m.Opcode, now);
        var p = m.Payload;
        switch (m.Opcode)
        {
            case 0x17D when p.Length >= 8:
            {
                var victim = BinaryPrimitives.ReadUInt32LittleEndian(p) & 0x7FFFFFFF;
                if (victim is >= 1 and <= MaxSlots || !_world.TryGet(victim, out var entry)) return;
                var (sector, x, y) = Position(entry);
                if (ObjectMirror.HitPoints(entry, out var hp, out var maxHp) && hp > 0)
                {
                    KillsAlive++;                                            // plan M5: a kill the server's health view does not back
                    Log.Info($"AUTH: witness: {s.Describe()} reports the death of 0x{victim:X}, which has {hp}/{maxHp} hit points in the server's world (last: {Trail(s)}).");
                }
                Witness!.OnKill(victim, sector, x, y, CompactLevel(entry.Payload[(entry.LongPrefix ? 24 : 8)..]), now);
                return;
            }
            case 0x016 when !s.InGame:
                Witness!.OnWorldStart(s.Slot, now);                         // the client builds the world around the hero (WorldStart source)
                return;
            case 0x017 or 0x018:
                Witness!.OnWorldEntered(s.Slot, now);
                return;
            case 0x13F when p.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(p[8..]) == s.Slot:
            {
                // an item enters the hero's inventory: trader stock must have been paid for (the gold check removes it otherwise)
                var reference = BinaryPrimitives.ReadUInt32LittleEndian(p) & 0x7FFFFFFF;
                if (Witness!.TakeStock(s.Slot, reference, out var price)) Witness.OnPurchase(s.Slot, reference, price, now);
                else if (Witness.WorldStartItemAnchored(s.Slot, reference, now)) WorldStartItemInHero(s, reference);
                return;
            }
            case 0x145 or (>= 0x1B8 and <= 0x1BE):
                Witness!.OnQuestEvent(s.Slot, now);                         // a quest event: its reward items may follow (Quest source)
                return;
            case GoldContainerActivationCodec.Opcode when p.Length >= 4:
            {
                var container = BinaryPrimitives.ReadUInt32LittleEndian(p) & 0x7FFFFFFF;
                if (!_world.TryGet(container, out var entry)) return;
                var (sector, x, y) = Position(entry);
                Witness!.OnContainer(container, s.Slot, sector, x, y, now);
                return;
            }
            case GoldItemPickupCodec.RequestOpcode or GoldItemPickupCodec.TakenOpcode when p.Length >= 4:
                CreditGoldPile(s, BinaryPrimitives.ReadUInt32LittleEndian(p) & 0x7FFFFFFF, now);
                return;
        }
    }

    /// <summary>A gold pile (resources 0x140C..0x140F, amount = price) the player picks up, credited once per pile.</summary>
    private void CreditGoldPile(PlayerSession s, uint reference, long now)
    {
        if (!_world.TryGet(reference, out var entry) || entry.Payload.Length < 8) return;
        var type = BinaryPrimitives.ReadUInt32LittleEndian(entry.Payload[4..]);
        if (type is < 0x140C or > 0x140F || Item(reference) is not { } pile) return;
        foreach (var credited in _creditedPiles) if (credited == reference) return;
        _creditedPiles[_creditedPileNext] = reference;
        _creditedPileNext = (_creditedPileNext + 1) % _creditedPiles.Length;
        Gold!.Credit(s.Slot, pile.Price, now);
    }

    /// <summary>RememberRemovedItem: the owner destroyed one of its items (its records may come back); a gold pile counts as picked up.</summary>
    private void WitnessDestroyed(PlayerSession s, uint reference, uint type, ReadOnlySpan<byte> content)
    {
        var now = _clock.ElapsedMilliseconds;
        if (type is >= 0x140C and <= 0x140F) { CreditGoldPile(s, reference, now); return; }
        Witness!.OnDestroyed(s.Slot, type, content, now);
    }

    /// <summary>CompleteTrade: each side may now create the items the other offered, and has the other's gold coming.</summary>
    private void WitnessTrade(PlayerSession a, PlayerSession b)
    {
        if (!_config.Auth.Enabled) return;
        var now = _clock.ElapsedMilliseconds;
        void Give(PlayerSession to, TradeState from)
        {
            foreach (var reference in from.Offer)
            {
                if (reference == 0 || !_world.TryGet(reference & 0x7FFFFFFF, out var entry) || !ItemBody(entry, out var body)) continue;
                if (!SavedObjects.ReadLiveItem(body, out var records, out _)) continue;
                Witness!.OnTradeReceive(to.Slot, ItemWitness.Fnv(SavedObjects.LiveContent(body, records)), now);
            }
            Gold!.Credit(to.Slot, from.Gold, now);
        }
        Give(a, b.Trade);
        Give(b, a.Trade);
    }

    /// <summary>A 0x020 grant: the player streams the sector's ground items now.</summary>
    private void WitnessSectorOwned(PlayerSession s, ushort sector)
    {
        if (_config.Auth.Enabled) Witness!.OnSectorOwned(s.Slot, sector, _clock.ElapsedMilliseconds);
    }

    private void WitnessLeave(PlayerSession s)
    {
        _witness?.Forget(s.Slot);
        _goldSeen[s.Slot] = 0;
        _goldLedger?.Forget(s.Slot);
        _trail.Forget(s.Slot);
    }

    /// <summary>The strict item gate asks: where does this client-created item come from? Logged when nothing explains it.</summary>
    private ItemSource ExplainCreation(PlayerSession s, uint reference, uint type, ReadOnlySpan<byte> payload, int prefix, ReadOnlySpan<byte> body, int records, bool purchase = false)
    {
        var content = SavedObjects.LiveContent(body, records);
        var data = content[(records * ItemWitness.RecordBytes)..];
        ushort sector; int x, y;
        if (prefix == 24 && payload.Length >= 24)
            (sector, x, y) = ((ushort)BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]), BinaryPrimitives.ReadInt32LittleEndian(payload[8..]), BinaryPrimitives.ReadInt32LittleEndian(payload[12..]));
        else (sector, x, y) = (BinaryPrimitives.ReadUInt16LittleEndian(body[GoldObjectStateLayout.SectorOffset..]), BinaryPrimitives.ReadInt32LittleEndian(body[GoldObjectStateLayout.XOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[GoldObjectStateLayout.YOffset..]));
        var price = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        if (ItemPrices is { } prices && WitnessConfig.PriceCheck != StrictItemsMode.Off && (purchase || (x == 0 && y == 0)) && prices.TryExpected(type, data, out var expected))
        {
            if (ItemPrices.Deviates(price, expected))
            {
                PriceMismatches++;
                Log.Info($"AUTH: price: {s.Describe()}: item 0x{reference:X} (type 0x{type:X}) says {price} gold, the game's pricing gives {expected}"
                    + (WitnessConfig.PriceCheck == StrictItemsMode.On ? ", the computed price counts." : " (log mode, the stated price counts)."));
            }
            if (WitnessConfig.PriceCheck == StrictItemsMode.On) price = expected;
        }
        var facts = new ItemFacts(reference, type, data[25], data[6] & 15, sector, x, y, ItemWitness.Fnv(content), purchase, price);
        var now = _clock.ElapsedMilliseconds;
        var source = Witness!.Classify(s.Slot, facts, content, now);
        if (source == ItemSource.Purchase) Witness.OnPurchase(s.Slot, reference, price, now);
        else if (source == ItemSource.Stock) Witness.OnStock(s.Slot, reference, price);
        else if (source == ItemSource.WorldStart) Witness.OnWorldStartItem(s.Slot, reference);
        else if (source == ItemSource.Unexplained)
            Log.Info($"AUTH: witness: 0x{reference:X} lies in sector {sector} at {x},{y} (the player stands in sector {s.CurrentSector}, which belongs to #{SectorOwnerOf(s.CurrentSector)}), price {price}, level {data[25]}.");
        return source;
    }

    private string Trail(PlayerSession s) => _trail.Describe(s.Slot, _clock.ElapsedMilliseconds);

    /// <summary>CaptureHeroState: a new hero state of the player; its gold goes through the ledger. With AUTH_STRICT_GOLD=on an unexplained
    /// increase is written back to the explained figure in the server's copy of the state, so the hero is saved without it.</summary>
    private void WitnessHeroGold(PlayerSession s, LiveHero live, bool full)
    {
        if (!HeroSavedByServer(s.Slot)) return;
        var body = full ? live.FullBody.AsSpan(0, live.FullBodyLength) : live.Body.AsSpan(0, live.BodyLength);
        var at = GoldOffset(body, full);
        if (at < 0) return;
        var stored = WitnessGold(s, BinaryPrimitives.ReadUInt32LittleEndian(body[at..]));
        BinaryPrimitives.WriteUInt32LittleEndian(body[at..], stored);
    }

    /// <summary>One gold report (a hero state, or the 0x11B values update a real client sends about a second after a gold change and
    /// every 30 s). Spending pays for announced purchases (PumpPurchases) whatever AUTH_STRICT_GOLD says; returns the gold the server
    /// keeps: the report, or with AUTH_STRICT_GOLD=on the explained figure.</summary>
    private uint WitnessGold(PlayerSession s, uint reported)
    {
        if (!HeroSavedByServer(s.Slot)) return reported;
        var before = _goldSeen[s.Slot];
        _goldSeen[s.Slot] = reported;
        if (before > 0 && reported < before) Witness!.OnGoldSpent(s.Slot, before - reported, _clock.ElapsedMilliseconds);
        var mode = WitnessConfig.StrictGold;
        if (mode == StrictItemsMode.Off) return reported;
        var ledger = Gold!;
        var unexplained = ledger.Report(s.Slot, reported, _clock.ElapsedMilliseconds);
        if (unexplained > 0)
        {
            Log.Info($"AUTH: gold: {s.Describe()}: +{unexplained} gold nothing explains (reported {reported}, explained {ledger.Allowed(s.Slot)}; last: {Trail(s)})"
                + (mode == StrictItemsMode.On ? ", not saved." : " (log mode, accepted)."));
            if (mode == StrictItemsMode.On && WitnessConfig.CountViolations) Violation(s, $"gold +{unexplained} without a source");
        }
        var allowed = ledger.Allowed(s.Slot);
        if (mode != StrictItemsMode.On || allowed >= reported) return reported;
        if (unexplained > 0) GoldClamped++;
        return (uint)allowed;
    }

    /// <summary>Offset of the hero's gold in a stored hero body (compact word +0x46 or the full block's +62), or -1.</summary>
    private static int GoldOffset(ReadOnlySpan<byte> body, bool full)
    {
        var values = GoldCreatureStateLayout.PrefixSize;
        if (body.Length <= values + GoldCreatureValuesLayout.CompactMinimumSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[GoldCreatureStateLayout.MarkerOffset..]) != GoldCreatureStateLayout.Marker ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[values..]) != GoldCreatureValuesLayout.Marker) return -1;
        if (full) return body.Length >= values + GoldCreatureValuesLayout.FullMinimumSize ? values + GoldCreatureValuesLayout.FullBlock08Offset + 62 : -1;
        var at = values + GoldCreatureValuesLayout.CompactWord46Offset;
        return at + 4 <= body.Length ? at : -1;
    }

    private byte SectorOwnerOf(ushort sector) => sector < SectorTableSize ? _sectorOwner[sector] : (byte)0;
}
