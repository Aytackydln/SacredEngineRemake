using System.Globalization;
using Sacred.GameServer.Auth;

namespace Sacred.GameServer.Simulation;

/// <summary>Where an item the client created came from, as far as the server can tell (docs/authoritative-world-plan.md, M1).</summary>
public enum ItemSource : byte
{
    /// <summary>Nothing the server saw explains it.</summary>
    Unexplained,
    /// <summary>A one-time token a script minted (<c>server.item_token</c>).</summary>
    Token,
    /// <summary>An item of the hero block the lobby delivered.</summary>
    Baseline,
    /// <summary>Loot near a creature the server saw die (0x17D) shortly before.</summary>
    Kill,
    /// <summary>Content of a container (0x18C) the server saw being opened shortly before.</summary>
    Container,
    /// <summary>An item the trade partner offered in a trade the server completed (0x198).</summary>
    Trade,
    /// <summary>Made from items the same player destroyed shortly before (picked up again, socketed, split, recombined).</summary>
    Rebuild,
    /// <summary>An item lying in a sector the player just took over (the owner streams the sector's ground items).</summary>
    Sector,
    /// <summary>An object of the world the player's client builds while it starts the game (between its upload begin 0x016 and the world start
    /// 0x017/0x018 plus a short grace): the ground items, creature equipment and merchant stock of the sectors it loads first.</summary>
    WorldStart,
    /// <summary>An item the client announced with 0x16D (a merchant purchase) and that the hero gold then paid for (checked later).</summary>
    Purchase,
    /// <summary>An item created shortly after a quest event of the same player (0x145 quest pair, 0x1B8..0x1BE journal and progress): quest rewards are
    /// created by the client that runs the quest script.</summary>
    Quest,
    /// <summary>A positionless item no hero holds yet (trader stock the sector owner creates when a hero comes near a merchant). Not judged when
    /// it is created; judged when it enters a hero (0x13F into the hero's inventory), where a payment must explain it (<see cref="Purchase"/>).</summary>
    Stock,
}

/// <summary>
/// Strictness of the witness checks of the authoritative mode (Closed Net, the hero kept by the server). Everything here is
/// configurable, keys <c>AUTH_*</c> in Gameserver.cfg or <c>--auth-*</c> on the command line:
/// <list type="bullet">
/// <item><c>AUTH_STRICT_ITEMS=off|log|on</c> (in <see cref="AuthoritativeOptions"/>): what happens to an item creation nothing explains.</item>
/// <item><c>AUTH_ITEM_SOURCES</c>: the sources the server accepts as an explanation, comma separated (default all:
/// <c>token,baseline,kill,container,trade,rebuild,sector,worldstart,purchase,quest,stock</c>). A source left out explains nothing, so its items fall under
/// <c>AUTH_STRICT_ITEMS</c>.</item>
/// <item>Windows, ranges and budgets of each source (below).</item>
/// <item><c>AUTH_STRICT_GOLD=off|log|on</c>: a gold increase of the hero nothing explains (gold piles picked up, trades) is ignored,
/// logged, or not saved (the hero is saved with the explained gold only).</item>
/// <item><c>AUTH_STRICT_VIOLATIONS=1</c>: refused items and gold count as violations towards <c>AUTH_MAX_VIOLATIONS</c> (default 0:
/// refused, not counted).</item>
/// </list>
/// </summary>
public sealed class WitnessOptions
{
    public const ItemSourceSet AllSources = ItemSourceSet.Token | ItemSourceSet.Baseline | ItemSourceSet.Kill | ItemSourceSet.Container
        | ItemSourceSet.Trade | ItemSourceSet.Rebuild | ItemSourceSet.Sector | ItemSourceSet.WorldStart | ItemSourceSet.Purchase | ItemSourceSet.Quest | ItemSourceSet.Stock;

    /// <summary>Sources accepted as an explanation.</summary>
    public ItemSourceSet Sources { get; set; } = AllSources;

    /// <summary>Loot counts for <see cref="LootWindowMs"/> after the kill, within <see cref="LootRange"/> world units of the creature
    /// (a sector is about 3435 units), at most <see cref="LootPerKill"/> items per creature.</summary>
    public int LootWindowMs { get; set; } = 5000;
    public int LootRange { get; set; } = 1500;
    public int LootPerKill { get; set; } = 8;
    /// <summary>Item level at most the creature's level plus this margin (0 = not checked; the mapping is not verified yet).</summary>
    public int LootLevelMargin { get; set; }

    public int ContainerWindowMs { get; set; } = 5000;
    public int ContainerRange { get; set; } = 800;
    public int ContainerPerOpen { get; set; } = 8;

    /// <summary>Items received in a trade must appear within this time after the trade was completed.</summary>
    public int TradeWindowMs { get; set; } = 30_000;
    /// <summary>Destroyed items may come back (rebuilt) within this time.</summary>
    public int RebuildWindowMs { get; set; } = 30_000;
    /// <summary>Ground items of a sector the player just took over count for this time.</summary>
    public int SectorWindowMs { get; set; } = 4000;
    /// <summary>The world start window stays open this long after the world start (0x017/0x018) was answered ...</summary>
    public int WorldStartGraceMs { get; set; } = 3000;
    /// <summary>... and closes by itself this long after its begin (0x016) if the world start never comes.</summary>
    public int WorldStartMaxMs { get; set; } = 120_000;
    /// <summary>A purchase must be paid for by a drop of the hero gold within this time ...</summary>
    public int PurchaseWindowMs { get; set; } = 15_000;
    /// <summary>... of at least this percentage of the item price.</summary>
    public int PurchasePercent { get; set; } = 25;
    /// <summary>An item admitted on a token with more value records than its creation had (sockets are added by a later update) must match the
    /// token's records within this time.</summary>
    public int TokenCompleteMs { get; set; } = 10_000;
    /// <summary>The price of a purchase or trader stock item: <c>off</c> trusts the price in the item's data, <c>log</c> (default) logs where it
    /// differs from the computed one (<c>--game-dir</c> needed), <c>on</c> uses the computed price for the payment check.</summary>
    public StrictItemsMode PriceCheck { get; set; } = StrictItemsMode.Log;
    /// <summary>A quest event explains up to <see cref="QuestPerEvent"/> item creations of the same player within <see cref="QuestWindowMs"/>.</summary>
    public int QuestWindowMs { get; set; } = 5000;
    public int QuestPerEvent { get; set; } = 4;

    public StrictItemsMode StrictGold { get; set; }
    /// <summary>Gold credits (piles picked up, trade gold) expire after this time without a matching increase.</summary>
    public int GoldWindowMs { get; set; } = 60_000;

    /// <summary>Refused items and gold count as violations.</summary>
    public bool CountViolations { get; set; }

    public bool Accepts(ItemSource source) => source switch
    {
        ItemSource.Token => (Sources & ItemSourceSet.Token) != 0,
        ItemSource.Baseline => (Sources & ItemSourceSet.Baseline) != 0,
        ItemSource.Kill => (Sources & ItemSourceSet.Kill) != 0,
        ItemSource.Container => (Sources & ItemSourceSet.Container) != 0,
        ItemSource.Trade => (Sources & ItemSourceSet.Trade) != 0,
        ItemSource.Rebuild => (Sources & ItemSourceSet.Rebuild) != 0,
        ItemSource.Sector => (Sources & ItemSourceSet.Sector) != 0,
        ItemSource.WorldStart => (Sources & ItemSourceSet.WorldStart) != 0,
        ItemSource.Purchase => (Sources & ItemSourceSet.Purchase) != 0,
        ItemSource.Quest => (Sources & ItemSourceSet.Quest) != 0,
        ItemSource.Stock => (Sources & ItemSourceSet.Stock) != 0,
        _ => false,
    };

    public static StrictItemsMode ParseMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "log" => StrictItemsMode.Log,
        "0" or "off" or "" or "false" => StrictItemsMode.Off,
        _ => StrictItemsMode.On,
    };

    /// <summary>Parses a source list ("kill,trade", "all", "none"); false with an unknown name.</summary>
    public static bool TryParseSources(string value, out ItemSourceSet sources)
    {
        sources = 0;
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "all": sources |= AllSources; break;
                case "none": break;
                case "token": sources |= ItemSourceSet.Token; break;
                case "baseline": sources |= ItemSourceSet.Baseline; break;
                case "kill" or "loot": sources |= ItemSourceSet.Kill; break;
                case "container" or "chest": sources |= ItemSourceSet.Container; break;
                case "trade": sources |= ItemSourceSet.Trade; break;
                case "rebuild" or "craft": sources |= ItemSourceSet.Rebuild; break;
                case "sector": sources |= ItemSourceSet.Sector; break;
                case "worldstart" or "start": sources |= ItemSourceSet.WorldStart; break;
                case "purchase" or "merchant" or "buy": sources |= ItemSourceSet.Purchase; break;
                case "quest": sources |= ItemSourceSet.Quest; break;
                case "stock" or "trader": sources |= ItemSourceSet.Stock; break;
                default: return false;
            }
        }
        return true;
    }

    /// <summary>Applies one config key (upper case, '-' as '_'). False for keys it does not know.</summary>
    public bool Apply(string key, string value)
    {
        switch (key)
        {
            case "AUTH_ITEM_SOURCES":
                if (TryParseSources(value, out var sources)) Sources = sources;
                else Log.Info($"AUTH_ITEM_SOURCES: unknown source in '{value}', kept {Sources}.");
                return true;
            case "AUTH_LOOT_WINDOW_MS": LootWindowMs = NonNegative(value, LootWindowMs); return true;
            case "AUTH_LOOT_RANGE": LootRange = NonNegative(value, LootRange); return true;
            case "AUTH_LOOT_PER_KILL": LootPerKill = NonNegative(value, LootPerKill); return true;
            case "AUTH_LOOT_LEVEL_MARGIN": LootLevelMargin = NonNegative(value, LootLevelMargin); return true;
            case "AUTH_CONTAINER_WINDOW_MS": ContainerWindowMs = NonNegative(value, ContainerWindowMs); return true;
            case "AUTH_CONTAINER_RANGE": ContainerRange = NonNegative(value, ContainerRange); return true;
            case "AUTH_CONTAINER_PER_OPEN": ContainerPerOpen = NonNegative(value, ContainerPerOpen); return true;
            case "AUTH_TRADE_WINDOW_MS": TradeWindowMs = NonNegative(value, TradeWindowMs); return true;
            case "AUTH_REBUILD_WINDOW_MS": RebuildWindowMs = NonNegative(value, RebuildWindowMs); return true;
            case "AUTH_SECTOR_WINDOW_MS": SectorWindowMs = NonNegative(value, SectorWindowMs); return true;
            case "AUTH_WORLDSTART_GRACE_MS": WorldStartGraceMs = NonNegative(value, WorldStartGraceMs); return true;
            case "AUTH_WORLDSTART_MAX_MS": WorldStartMaxMs = NonNegative(value, WorldStartMaxMs); return true;
            case "AUTH_PURCHASE_WINDOW_MS": PurchaseWindowMs = NonNegative(value, PurchaseWindowMs); return true;
            case "AUTH_PURCHASE_PERCENT": PurchasePercent = NonNegative(value, PurchasePercent); return true;
            case "AUTH_QUEST_WINDOW_MS": QuestWindowMs = NonNegative(value, QuestWindowMs); return true;
            case "AUTH_QUEST_PER_EVENT": QuestPerEvent = NonNegative(value, QuestPerEvent); return true;
            case "AUTH_PRICE_CHECK": PriceCheck = ParseMode(value); return true;
            case "AUTH_TOKEN_COMPLETE_MS": TokenCompleteMs = NonNegative(value, TokenCompleteMs); return true;
            case "AUTH_STRICT_GOLD": StrictGold = ParseMode(value); return true;
            case "AUTH_GOLD_WINDOW_MS": GoldWindowMs = NonNegative(value, GoldWindowMs); return true;
            case "AUTH_STRICT_VIOLATIONS": CountViolations = NonNegative(value, 1) != 0; return true;
        }
        return false;
    }

    private static int NonNegative(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? Math.Max(0, parsed) : fallback;
}

[Flags]
public enum ItemSourceSet : ushort
{
    Token = 1, Baseline = 2, Kill = 4, Container = 8, Trade = 16, Rebuild = 32, Sector = 64, WorldStart = 128, Purchase = 256, Quest = 512, Stock = 1024,
}
