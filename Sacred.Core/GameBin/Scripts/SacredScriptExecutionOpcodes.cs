namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold 0x475680 dispatch entries used by the mutable script executor.</summary>
public static class SacredScriptExecutionOpcodes
{
    /// <summary>Gold dispatcher returns 3; the function caller reloads and executes from its beginning.</summary>
    public const ushort RestartFunction = 111;
    /// <summary>Gold 0x465280: bind a type reference and qualifier to a dialog-registration slot.</summary>
    public const ushort AssociateDialog = 106;
    /// <summary>Gold 0x475F1A: append a positive signed type ID to a global equipment pool, index 0..254.</summary>
    public const ushort AddEquipmentPoolType = 114;
    /// <summary>Gold 0x491B30 parses all operands but skips placement in
    /// multiplayer. Single-player requires the original hero teleport/copy.</summary>
    public const ushort StartPosition = 45;
    public const ushort QuestBook = 53;
    /// <summary>Gold 0x49A4B0: update a quest's journal compass target or pending compass scratch.</summary>
    public const ushort QuestCompassPosition = 63;
    /// <summary>Gold 0x49AC80: named object/resource compass target, with
    /// optional rad: string and quest number. Missing journals retain deferred scratch.</summary>
    public const ushort QuestCompassObject = 64;
    /// <summary>Gold 0x4AE350: local player information; selector 5 is skipped in multiplayer.</summary>
    public const ushort PlayerInformation = 132;
    /// <summary>Gold 0x4A7760: resolve a dialog-registration name for the active hero.</summary>
    public const ushort Popup = 95;
    /// <summary>Gold 0x48DF30: clamp a base attribute to 0..100 and recalculate the current level.</summary>
    public const ushort SetCreatureBaseAttribute = 103;
    /// <summary>Gold 0x48E280: set current hit points as a clamped percentage of maximum.</summary>
    public const ushort SetCreatureHitPoints = 52;
    public const ushort TriggerQuest = 20;
    public const ushort ActivateQuest = 21;
    public const ushort CallFunction = 22;
    public const ushort DefinePosition = 23;
    public const ushort SetVariable = 67;
    /// <summary>Gold 0x49D450: select an inclusive integer range into an ordinary
    /// variable, preserving its signed bound sentinels and shared CRT RNG.</summary>
    public const ushort RandomVariable = 105;
    /// <summary>Gold 0x49B840: OR one bit per index into an ordinary variable; actor/hero families are separate.</summary>
    public const ushort SetVariableBits = 68;
    /// <summary>Dispatches to the same SetVar handler as opcode 67.</summary>
    public const ushort DefineVariable = 65;
    public const ushort AddVariable = 75;
    public const ushort DefinePoolPosition = 108;
    /// <summary>Gold 0x4B0C00: consume random pool definitions and register hideout mouseovers.</summary>
    public const ushort CreateHideouts = 140;
    /// <summary>Gold 0x4A9670: writes monster maximum count, minimum level and maximum level
    /// to the current sector environment. Each value is narrowed to UInt16.</summary>
    public const ushort SetSectorMonsterParameters = 100;
    /// <summary>Writes consecutive tagged integer operands to interpreter +0x7684.
    /// The installed startup supplies eight words; their individual gameplay meanings are unresolved.</summary>
    public const ushort SetStartupParameters = 107;
}
