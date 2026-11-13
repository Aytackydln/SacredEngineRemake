namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold interpreter additional variable globals. Reset zeroes all 150
/// Int32 words; native save/load transfers their complete 600-byte block.</summary>
public static class SacredScriptVariableGlobalsLayout
{
    public const int InterpreterOffset = 0x7750;
    public const int Count = 150;
    public const int Size = Count * sizeof(int);
    // SetVar 0x49B453 / AddVar use atoi on the caller's scratch name +7.
    public const int CallerNameSize = 32;
    public const int IndexSuffixOffset = 7;
    // Gold 0x482572..0x482584: owner text scratch in the two-argument NPC caller.
    // The next single-argument variable caller's +7 aliases owner byte 231.
    public const int CreatureOwnerNameSize = 256;
    public const int CreatureOwnerSuffixOffset = 231;
}
