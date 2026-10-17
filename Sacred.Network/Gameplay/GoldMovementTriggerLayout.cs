namespace Sacred.Network.Gameplay;

/// <summary>Gold sector movement-trigger record, uploaded in bank 0x1B2 section 2.
/// Original append helpers: client 0x622E80, GameServer 0x478920.</summary>
public static class GoldMovementTriggerLayout
{
    public const ushort BankOpcode = GoldWorldBankLayout.Bank2Opcode;
    public const int BankSection = 2;
    public const int Size = 76;
    public const int FirstXOffset = 0;
    public const int FirstYOffset = 2;
    public const int SurfaceOffset = 4;
    public const int LastXOffset = 6;
    public const int LastYOffset = 8;
    public const int RetainedWordOffset = 10;
    public const int NameOffset = 12;
    public const int NameSize = 64;
    public const int SectorWidth = 64;
}
