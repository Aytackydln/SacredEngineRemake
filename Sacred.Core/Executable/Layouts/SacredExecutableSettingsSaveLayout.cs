namespace Sacred.Core.Executable.Layouts;

public static class SacredExecutableSettingsSaveLayout
{
    public static ReadOnlySpan<byte> Signature => [0x83, 0xec, 0x4c, 0x55, 0x57, 0x8b, 0xf9, 0x68];
    public const int SignatureOffset = 0x15;
    public const int OpenModeOperand = 0x1d;
    public const int RecordFormatOperand = 0x124;
}
