namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold instruction dispatcher 0x475680 and its byte-offset branch scanners.</summary>
public static class SacredScriptControlOpcodes
{
    public const ushort Stop = 0;
    public const ushort If = 58;
    public const ushort BranchEnd = 59;
    public const ushort Nop = 62;
    public const ushort ElseIf = 66;
    public const int MaximumOpcode = 140;
    public const int MaximumInstructionBytes = 2048;
    public const int MaximumScannedInstructionBytes = 500;
}
