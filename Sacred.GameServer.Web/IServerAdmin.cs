using System.Text.Json.Serialization;

namespace Sacred.GameServer.Web;

/// <summary>What the web UI may do and see. Implemented by the game server; every member is safe to call from any thread.</summary>
public interface IServerAdmin
{
    ServerOverview Overview();
    IReadOnlyList<PlayerInfo> Players();
    /// <summary>Id of the newest chat entry (0 when none yet).</summary>
    long ChatHead { get; }
    /// <summary>Entries with an id greater than <paramref name="afterId"/>, oldest first (at most <paramref name="max"/>).</summary>
    IReadOnlyList<ChatEntry> ChatSince(long afterId, int max);
    IReadOnlyList<BanEntry> Bans();
    /// <summary>Current flood protection limits and counters.</summary>
    FloodSettings Flood();
    /// <summary>What the server knows about each online hero (refreshed about once a second).</summary>
    IReadOnlyList<HeroInfo> Heroes();
    /// <summary>HTTP endpoints registered by server scripts (<c>server.endpoint</c>).</summary>
    IReadOnlyList<ModEndpointInfo> ModEndpoints();
    /// <summary>Every installed mod (script file), with its description, load error, pages and endpoints.</summary>
    IReadOnlyList<ModInfo> Mods() => ModEndpoints().GroupBy(e => e.Mod).Select(g => new ModInfo(g.Key, null, null, g.ToArray())).ToArray();
    /// <summary>Runs a script endpoint on the game loop. <paramref name="publicOnly"/>: only endpoints the script marked public.</summary>
    Task<ModResponse> ModRequest(string mod, string path, IReadOnlyList<KeyValuePair<string, string>> query, bool publicOnly, CancellationToken cancellationToken);
    /// <summary>The area editor's data: the sector table and the world map of --game-dir. <paramref name="image"/>: the map as a PNG, else
    /// the sectors and the map projection as JSON. Built once, off the game loop; 404 without game data.</summary>
    Task<BinaryResponse> AreaMapRequest(bool image, CancellationToken cancellationToken) =>
        Task.FromResult(new BinaryResponse(404, "text/plain; charset=utf-8", "the area map needs --game-dir"u8.ToArray()));
    /// <summary>Reads (<paramref name="body"/> null) or writes the storage of a mod (script) on the game loop; see <c>POST /api/mod-store</c>.</summary>
    Task<ModResponse> ModStoreRequest(string mod, string? key, string? body, CancellationToken cancellationToken) =>
        Task.FromResult(new ModResponse(404, "text/plain; charset=utf-8", "scripts are off"));
    /// <summary>Queues a command for the game loop. False when the queue is full.</summary>
    bool Submit(in AdminCommand command);
    /// <summary>Cancelled when the server stops: the web UI ends its streaming responses and stops at once (it never waits for browsers).</summary>
    CancellationToken StopRequested { get; }
}

public sealed record ServerOverview(
    string Name, long Tick, double UptimeSeconds, int TcpPort, int UdpPort, int Players, int MaxPlayers, bool Locked, bool Paused,
    bool PasswordSet, bool WorldAvailable, uint GameFlags, int Mode, int Level, bool LobbyConfigured, bool LobbyOnline, string? LobbyHost, string Version);

public sealed record PlayerInfo(
    int Slot, string Name, string Address, string Transport, bool IsHost, bool InGame, bool Ready, bool PasswordAccepted, uint LatencyMs,
    double ConnectedSeconds);

public enum ChatKind { Public = 0, Private = 1, Broadcast = 2, ServerMessage = 3, System = 4 }

/// <summary>One line of the live log. FromSlot/ToSlot 0 means the server; ToSlot -1 means everybody.</summary>
public sealed record ChatEntry(long Id, long TimeUnixMs, ChatKind Kind, int FromSlot, string FromName, int ToSlot, string Text);

public sealed record BanEntry(string Address, string Reason, long CreatedUnixMs);

public enum AdminCommandKind
{
    None = 0,
    /// <summary>Text to every player.</summary>
    Broadcast,
    /// <summary>Slot, Text: private message from the server.</summary>
    Message,
    /// <summary>Slot, Value = kick reason.</summary>
    Kick,
    /// <summary>Slot, Text = reason: ban the player's address and kick it.</summary>
    Ban,
    /// <summary>Text = address to ban (no player needed).</summary>
    BanAddress,
    /// <summary>Text = address to lift.</summary>
    Unban,
    /// <summary>Value 1 = lock the session against new players, 0 = unlock.</summary>
    Lock,
    /// <summary>Value 1 = pause the game, 0 = continue.</summary>
    Pause,
    SetMaxPlayers,
    /// <summary>Text = new password; empty removes it.</summary>
    SetPassword,
    SetName,
    /// <summary>Value = mode (0..4), Slot = level (0..5); only before the host world exists.</summary>
    SetMode,
    /// <summary>Kick everybody and reset the session.</summary>
    EndSession,
    Shutdown,
    /// <summary>Text = <c>KEY=VALUE</c> pairs separated by ';' or new lines (FLOOD_* keys, MAX_CONNECTIONS_PER_ADDRESS); applied live.</summary>
    SetFlood,
    /// <summary>Text = mod name (script file without .lua) or empty for every mod: its "unload" handler runs, then it is loaded again from disk.</summary>
    ReloadMods,
}

/// <summary>Flood protection as the game loop uses it right now (see Sacred.GameServer README "Flood protection").</summary>
public sealed record FloodSettings(
    bool Enabled, bool Explicit, int MessagesPerSecond, int MessageBurst, int BytesPerSecond, int ByteBurst, int ChatPerSecond, int ChatBurst,
    int KickAfterDrops, int StrikeWindowSeconds, int UploadGraceSeconds, int MaxConnectionsPerAddress, long Drops, long Removed, long RefusedConnections);

/// <summary>An online hero. Values come from the hero's own object states and inventory messages; <see cref="Attributes"/> is
/// null until the client sent a full state (0x16B). Skill, combat art and item ids are the game's numbers (names need its text
/// tables: the optional Name fields). <see cref="Account"/> is only filled for the admin API.</summary>
public sealed record HeroInfo(int Slot, string Character, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Account,
    int Class, int Level, long Experience, long Gold, int SkillPointsLeft, SkillInfo[] Skills, int[] AttributePoints, int[]? Attributes,
    ArtInfo[] CombatArts, int Combos, EquippedItem[] Equipment, StoredItem[] Inventory, StoredItem[] Stash, bool InGame, double OnlineSeconds);

/// <summary>A skill slot. <see cref="Name"/> (and the other records' names) is the game's text when the server read its tables
/// (--game-dir), else left out.</summary>
public sealed record SkillInfo(int Id, int Level, int Bonus, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);
/// <summary>A combat art record (22 bytes): kind word (1 spell, 2 special move), art id and the byte after it (its rank as far as the saves show).</summary>
public sealed record ArtInfo(int Kind, int Id, int Rank, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);
/// <summary>An item: object reference, type (Items.pak resource), item level, rarity tier, filled sockets, price and its name
/// (its own generated name, else the type's).</summary>
public sealed record ItemInfo(uint Ref, uint Type, int Level, int Rarity, int Sockets, long Price, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);
public sealed record EquippedItem(int Slot, ItemInfo Item);
public sealed record StoredItem(int Cell, int Width, int Height, ItemInfo Item);

/// <summary>A script endpoint: <c>/mods/{Mod}/{Path}</c> (and <c>/public/mods/...</c> when <see cref="Public"/>).</summary>
public sealed record ModEndpointInfo(string Mod, string Path, bool Public, string? Description, bool Page = false);
/// <summary>An installed mod: its name (script file without .lua), short description, load error and its pages and endpoints.</summary>
public sealed record ModInfo(string Mod, string? Description, string? Error, ModEndpointInfo[] Items);

/// <summary>A script endpoint's answer.</summary>
public sealed record ModResponse(int Status, string ContentType, string Body, bool Page = false);

/// <summary>An answer with a binary body (the area map PNG).</summary>
public sealed record BinaryResponse(int Status, string ContentType, byte[] Body);

public readonly record struct AdminCommand(AdminCommandKind Kind, int Slot = 0, int Value = 0, string? Text = null);
