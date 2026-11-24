namespace Sacred.GameServer.Scripting;

/// <summary>An item as the server sees it with one hero (<c>server.hero_item</c>): where it is (<c>backpack</c>: the hero's own
/// inventory, <c>stash</c>: the shared inventory 17, <c>equipped</c>, <c>world</c>: an object of the player in no inventory,
/// <c>gone</c>: destroyed by its owner, 0x167, kept for a while), its type and its value records (the item's own 130-byte block
/// after one per socketed rune/gem, lower-case hex: the same bytes Sacred Gear's <c>gear.inv.export</c> carries).</summary>
public readonly record struct ScriptHeroItem(uint Reference, uint Type, string Where, string Records, double AgeSeconds = 0);

/// <summary>What scripts need from an authoritative server that keeps the heroes (Closed Net, <c>--authoritative</c>): the server's
/// view of a hero's items, items the player destroyed, and a hero save the lobby confirmed. The defaults: not available.</summary>
public partial interface IScriptServer
{
    /// <summary>True when the server writes this player's hero (authoritative mode, a Closed Net hero, the lobby online).</summary>
    bool HeroSavedByServer(int slot) => false;
    /// <summary>One item of the player by its object reference: in an inventory, equipped, in the world, or recently destroyed.</summary>
    ScriptHeroItem? HeroItem(int slot, uint reference) => null;
    /// <summary>The items in the hero's inventories and equipment as the server sees them.</summary>
    IReadOnlyList<ScriptHeroItem> HeroItems(int slot) => [];
    /// <summary>Takes a destroyed item off the "gone" list, so it is claimed once (true when it was there).</summary>
    bool ClaimGoneItem(int slot, uint reference) => false;
    /// <summary>A one-time permission for the client of <paramref name="slot"/> to create an item of this type with these value records (hex);
    /// returns its id, or 0 (not authoritative, bad arguments). It lives <paramref name="ttlSeconds"/> s and is bound to the player's session,
    /// lobby account and character.</summary>
    long MintItemToken(int slot, uint type, string records, double ttlSeconds, string label) => 0;
    /// <summary>Removes a token that was not used.</summary>
    bool RevokeItemToken(long id) => false;
    /// <summary>Saves the hero now (the server's own view, as the anti-dupe saves); null when the lobby stored it, else why not.</summary>
    Task<string?> SaveHeroNow(int slot) => Task.FromResult<string?>("this server does not save heroes (it needs --authoritative and Closed Net)");
    /// <summary>Unix seconds of the last hero upload sent for this player's hero (kept across sessions while the server runs), or null.</summary>
    double? HeroUploadTime(int slot) => null;
}
