namespace Sacred.Core.Executable.Layouts;

/// <summary>Gold importer calls/string-getter operands and inspected pureHD calls.
/// Literal pointers describe ANSI construction; range keys have independent
/// strlen input, range end-base and range start operands.</summary>
public static class SacredExecutableSessionStringLayout
{
    public static int GoldCall(SacredSessionStringSetting setting) => setting switch
    {
        SacredSessionStringSetting.CdKey => 0xef, SacredSessionStringSetting.CdKey2 => 0x1e7,
        SacredSessionStringSetting.Ip => 0x2e2, SacredSessionStringSetting.Lobby => 0x3fb,
        SacredSessionStringSetting.Login => 0x453, SacredSessionStringSetting.LobbyPassword => 0x4cc,
        SacredSessionStringSetting.Password => 0x572, SacredSessionStringSetting.Player => 0x5e4,
        SacredSessionStringSetting.Session => 0x67b, SacredSessionStringSetting.AseAddress => 0x75a,
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };
    public static int PureHdCall(SacredSessionStringSetting setting) => setting switch
    {
        SacredSessionStringSetting.Ip => 0x37f, SacredSessionStringSetting.Lobby => 0x498,
        SacredSessionStringSetting.Login => 0x4f0, SacredSessionStringSetting.LobbyPassword => 0x568,
        SacredSessionStringSetting.Password => 0x60e, SacredSessionStringSetting.Player => 0x67c,
        SacredSessionStringSetting.Session => 0x714,
        _ => throw new NotSupportedException("This installed importer getter requires a separate native comparison.")
    };
    public const int LiteralReadKeyOperand = 0x2b;
    public const int DefaultWriteLiteralOperand = 0xba;
    public const int DefaultKeyOperand = 0xf3;
    public const int DefaultReturnLiteralOperand = 0x161;
    public const int DirectKeyOperand = 0x4b;
    public const int AddressKeyOperand = 0x39;
    public const int AddressEmptyReturnOperand = 0x12d;
    public const int CdKeyReadOperand = 0x35;
    public const int CdKeyComparisonLengthOperand = 0x24a;
    public const int CdKeySeparatorOperand = 0x253;
    public const int CdKeyReplacementOperand = 0x313;
    public const int CdKeyEmptyReturnOperand = 0x378;
    public const int KeyEndBaseDistance = 0x15;
    public const int KeyRangeStartDistance = 0x1b;
}
