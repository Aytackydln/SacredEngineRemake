namespace Sacred.Core.Executable.Layouts;

/// <summary>Importer-relative call operands and numeric getter instructions.
/// Imm8 defaults are sign extended; no-default ASE exits execute xor eax,eax.</summary>
public static class SacredExecutableSessionIntegerLayout
{
    public const int GoldLobbyPortCall = 0x548;
    public const int GoldSpeedCall = 0x6fd;
    public const int GoldAsePortCall = 0x7f2;
    public const int PureHdLobbyPortCall = 0x5e4;
    public const int PureHdSpeedCall = 0x797;
    public const int ReadKeyOperand = 0x30;
    public const int DefaultKeyOperand = 0xff;
    public const int KeyEndBaseOperandDistance = 0x15;
    public const int KeyRangeStartOperandDistance = 0x1b;
    public const int DefaultPushOffset = 0x14c;
    public const int Imm32ReturnOperand = 0x19d;
    public const int Imm8ReturnOperand = 0x19a;
    public const int AseZeroReturnOffset = 0x139;
}
