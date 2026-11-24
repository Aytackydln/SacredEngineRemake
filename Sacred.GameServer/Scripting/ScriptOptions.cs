using System.Globalization;

namespace Sacred.GameServer.Scripting;

/// <summary>Server-side Lua scripts (<c>SCRIPTS_*</c> keys). Scripts run when the scripts folder exists, unless <c>SCRIPTS=0</c>.</summary>
public sealed class ScriptOptions
{
    /// <summary>Explicit switch; null = on when <see cref="Directory"/> exists.</summary>
    public bool? Enabled { get; set; }
    /// <summary>Folder with the <c>*.lua</c> files, loaded in name order and reloaded when one changes.</summary>
    public string Directory { get; set; } = "scripts";
    /// <summary>Folder for the mods' own storage: one <c>{mod}.json</c> per script (<c>server.save</c>/<c>server.set</c>, ...).</summary>
    public string DataDirectory { get; set; } = "script-data";
    /// <summary>Largest single value a mod may save (UTF-8 bytes of its JSON).</summary>
    public int MaxStoreChars { get; set; } = 4 * 1024 * 1024;
    /// <summary>Largest storage of one mod (UTF-8 bytes of its JSON, about the file size); saves beyond it fail with a script error.</summary>
    public long MaxModStoreChars { get; set; } = 8 * 1024 * 1024;
    /// <summary>Largest storage of all mods together.</summary>
    public long MaxTotalStoreChars { get; set; } = 32 * 1024 * 1024;
    /// <summary>Lua instructions one event handler or timer may run before it is stopped (a mistake such as an endless loop
    /// must not freeze the game).</summary>
    public int InstructionBudget { get; set; } = 1_000_000;
    /// <summary>Largest answer of a script endpoint in characters (larger: 500 "response too large").</summary>
    public int MaxResponseChars { get; set; } = 2 * 1024 * 1024;
    /// <summary>Largest string string.rep may build. Concatenation cannot be capped by LuaCSharp; the instruction budget
    /// bounds how often a script can double a string in one call.</summary>
    public int MaxStringChars { get; set; } = 4 * 1024 * 1024;

    public bool IsEnabled => Enabled ?? System.IO.Directory.Exists(Directory);

    public bool Apply(string key, string value)
    {
        switch (key)
        {
            case "SCRIPTS": Enabled = Int(value, 1) != 0; return true;
            case "SCRIPTS_DIR" or "SCRIPTS_DIRECTORY": Directory = value; return true;
            case "SCRIPTS_DATA": DataDirectory = value; return true;
            case "SCRIPTS_STORE_MAX": MaxModStoreChars = Math.Clamp(Int(value, (int)Math.Min(MaxModStoreChars, int.MaxValue)), 64 * 1024, int.MaxValue); return true;
            case "SCRIPTS_STORE_TOTAL_MAX": MaxTotalStoreChars = Math.Clamp(Int(value, (int)Math.Min(MaxTotalStoreChars, int.MaxValue)), 64 * 1024, int.MaxValue); return true;
            case "SCRIPTS_MAX_RESPONSE": MaxResponseChars = Math.Clamp(Int(value, MaxResponseChars), 1024, 64 * 1024 * 1024); return true;
            case "SCRIPTS_MAX_STRING": MaxStringChars = Math.Clamp(Int(value, MaxStringChars), 1024, 256 * 1024 * 1024); return true;
            case "SCRIPTS_BUDGET": InstructionBudget = Math.Clamp(Int(value, InstructionBudget), 10_000, 100_000_000); return true;
        }
        return false;
    }

    private static int Int(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
