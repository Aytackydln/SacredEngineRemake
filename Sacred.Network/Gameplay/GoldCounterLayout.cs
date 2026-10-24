namespace Sacred.Network.Gameplay;

/// <summary>Gold client counter/message receives at 0x7E0055..0x7E02EA.</summary>
public static class GoldCounterLayout
{
    public const ushort MessageOpcode = 0x19E;
    public const ushort KillNoticeOpcode = 0x19F;
    public const ushort CollectNoticeOpcode = 0x1A0;
    public const ushort KillOpcode = 0x1A1;
    public const ushort CollectOpcode = 0x1A2;
    public const ushort CollectAndCountOpcode = 0x1A3;
    public const int RecordSize = 76;
    public const int NameOffset = 12;
    public const int NameSize = 64;
    public const int KillBankSection = 7;
    public const int CollectBankSection = 8;
    public const int MappingBankSection = 9;
    public const uint ResourceLimit = 0x7e60;
    public const uint MaximumRemaining = 200000;
    public const uint KillCompleteText = 0x44df;
    public const uint CollectCompleteText = 0x44eb;
}
