using System.Globalization;

namespace Sacred.GameServer.Auth;

/// <summary>
/// Settings of the optional authoritative mode (<c>--authoritative</c>). Normal relay games are unaffected: without the switch
/// no authoritative code path runs. Keys use the same KEY=VALUE form as <c>Gameserver.cfg</c> (prefix <c>AUTH_</c>).
/// </summary>
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
            case "AUTH_SAVE_DEBOUNCE_MS": SaveDebounceMilliseconds = Math.Max(0, Int(value, SaveDebounceMilliseconds)); return true;
        }
        return false;
    }

    private static int Int(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
