namespace Sacred.Network.Admission;

/// <summary>Gold opcode 0x1B: exact size checked by GameServer 0x4D2EE0.</summary>
public static class GoldPlayerDataLayout
{
    public const ushort Opcode = 0x1B;
    public const int Size = 834;
    public const int CharacterTypeOffset = 4;
    public const int ProfileByte5Offset = 5;
    /// <summary>UInt32 setting, copied to native roster +0x60 (record base +0x54). Only 1 is low bandwidth.</summary>
    public const int ConnectionSpeedOffset = 0x0C;
    public const int VersionOffset = 0x10;
    public const int LobbyIdentityOffset = 0x12;
    public const int NameOffset = 0x16;
    public const int NameBytes = 256;
    public const int ProfileOffset = 0x116;
    public const int ProfileBytes = 556;
}
