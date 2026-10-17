namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold 0x475680 dispatch entries used by the mutable script executor.</summary>
public static class SacredScriptExecutionOpcodes
{
    public const ushort CallFunction = 22;
    public const ushort DefinePosition = 23;
    public const ushort SetVariable = 67;
    /// <summary>Dispatches to the same SetVar handler as opcode 67.</summary>
    public const ushort DefineVariable = 65;
    public const ushort AddVariable = 75;
    public const ushort DefinePoolPosition = 108;
    /// <summary>Gold 0x4A9670: writes monster maximum count, minimum level and maximum level
    /// to the current sector environment. Each value is narrowed to UInt16.</summary>
    public const ushort SetSectorMonsterParameters = 100;
    /// <summary>Writes consecutive tagged integer operands to interpreter +0x7684.
    /// The installed startup supplies eight words; their individual gameplay meanings are unresolved.</summary>
    public const ushort SetStartupParameters = 107;
}
