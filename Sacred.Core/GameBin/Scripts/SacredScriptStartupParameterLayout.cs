namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold opcode 107 writes consecutive Int32 parameters at interpreter
/// +0x7684. The bank-1 writer at 0x7E91A4 transfers exactly the first 128 bytes
/// from this same block. Unwritten words require original initialization.</summary>
public static class SacredScriptStartupParameterLayout
{
    public const int InterpreterOffset = 0x7684;
    public const int BankBlockSize = 128;
    public const int BankWordCount = BankBlockSize / sizeof(int);
}
