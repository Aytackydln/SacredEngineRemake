namespace Sacred.Network.Gameplay;

/// <summary>Bulk script vectors at interpreter +0x7614/+0x7620/+0x762C.
/// Record field meanings remain unresolved; the first two tables are independent.</summary>
public static class GoldScriptTableLayout
{
    public const ushort Table1D3Opcode = 0x1D3;
    public const ushort Table1D4Opcode = 0x1D4;
    public const ushort Table1D5Opcode = 0x1D5;
    public static int GetRecordSize(ushort opcode) => opcode switch
    {
        Table1D3Opcode or Table1D4Opcode => 76,
        Table1D5Opcode => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(opcode))
    };
}
