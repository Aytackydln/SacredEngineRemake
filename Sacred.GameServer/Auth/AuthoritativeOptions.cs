using System.Globalization;

namespace Sacred.GameServer.Auth;

/// <summary>
/// Settings of the optional authoritative mode (<c>--authoritative</c>). Normal relay games are unaffected: without the switch
/// no authoritative code path runs. Keys use the same KEY=VALUE form as <c>Gameserver.cfg</c> (prefix <c>AUTH_</c>).
/// </summary>
public enum StrictItemsMode { Off, Log, On }

public sealed class AuthoritativeOptions
{
    /// <summary>Master switch.</summary>
    public bool Enabled { get; set; }
    /// <summary>Largest damage word a client may report with 0x17A; larger values are clamped. The receiver narrows it to 16 bits.</summary>
    public uint MaxDamage { get; set; } = 0xFFFF;
    /// <summary>Reject damage whose attacker reference is unknown to the server's object world (off: only known foreign attackers are rejected).</summary>
    public bool StrictAttackers { get; set; }
    /// <summary>Violations after which a player is kicked (0 = never).</summary>
    public int MaxViolations { get; set; } = 25;
    /// <summary>Keep the game difficulty bits (0x100..0x800) of the server configuration; the host's 0x01E cannot change them.</summary>
    public bool LockDifficulty { get; set; } = true;
    /// <summary>Interval of the periodic hero save to the lobby in seconds (0 = off).</summary>
    public int SaveIntervalSeconds { get; set; } = 120;
    /// <summary>Quiet time after a trade/pick-up before the changed hero is saved, in milliseconds.</summary>
    public int SaveDebounceMilliseconds { get; set; } = 1500;

    /// <summary>Refuse item states that no legitimate item has (sockets, item types against the game tables; Auth/ItemGuard.cs).</summary>
    public bool CheckItems { get; set; } = true;
    /// <summary>Items a player's client creates (0x16F / 0x16E of an object the server's world does not have) in Closed Net with the hero kept by
    /// the server: <see cref="StrictItemsMode.Off"/> (default), <c>Log</c> (counted and logged, still accepted) or <c>On</c> (refused: no state, never
    /// saved). Allowed: an item covered by a one-time token a script minted (<c>server.item_token</c>) and an item of the hero's baseline block.
    /// Experimental: the game's other legitimate creators (loot of client-side creatures, merchants, crafting, quest rewards) are not known yet.</summary>
    public StrictItemsMode StrictItems { get; set; }
    /// <summary>Witness strictness: accepted item sources, windows and budgets, the gold ledger (Simulation/WitnessOptions.cs).</summary>
    public Simulation.WitnessOptions Witness { get; } = new();
    /// <summary>Highest hero level a player's hero may report (Sacred Gold caps at 216; 0 = no check). Above it the player is removed.</summary>
    public int MaxHeroLevel { get; set; } = 216;

    /// <summary>Applies one config key (already upper-cased, '-' replaced by '_'). Returns false for unknown keys.</summary>
    public bool Apply(string key, string value)
    {
        switch (key)
        {
            case "AUTHORITATIVE": Enabled = Int(value, 1) != 0; return true;
            case "AUTH_MAX_DAMAGE": MaxDamage = (uint)Math.Clamp(Int(value, (int)MaxDamage), 0, int.MaxValue); return true;
            case "AUTH_STRICT_ATTACKERS": StrictAttackers = Int(value, 1) != 0; return true;
            case "AUTH_MAX_VIOLATIONS": MaxViolations = Math.Max(0, Int(value, MaxViolations)); return true;
            case "AUTH_LOCK_DIFFICULTY": LockDifficulty = Int(value, 1) != 0; return true;
            case "AUTH_SAVE_INTERVAL": SaveIntervalSeconds = Math.Max(0, Int(value, SaveIntervalSeconds)); return true;
            case "AUTH_CHECK_ITEMS": CheckItems = Int(value, 1) != 0; return true;
            case "AUTH_STRICT_ITEMS": StrictItems = Simulation.WitnessOptions.ParseMode(value); return true;
            case "AUTH_MAX_HERO_LEVEL": MaxHeroLevel = Math.Max(0, Int(value, MaxHeroLevel)); return true;
            case "AUTH_SAVE_DEBOUNCE_MS": SaveDebounceMilliseconds = Math.Max(0, Int(value, SaveDebounceMilliseconds)); return true;
        }
        return Witness.Apply(key, value);
    }

    private static int Int(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
