namespace Sacred.Network.Admission;

/// <summary>Gold opcode 0x10 payload, constructed by the client at 0x7D4FA0.</summary>
public static class GoldFirstContactLayout
{
    public const ushort Opcode = 0x10;
    public const int Size = 950;
    public const int LobbyIdentityOffset = 0;
    public const int VersionOffset = 4;
    public const int CharacterTypeOffset = 6;
    public const int ProfileByte7Offset = 7;
    /// <summary>NETWORK_SPEEDSETTINGS: 1 reduces ambient NPC detail; 2 is LAN/high bandwidth.</summary>
    public const int ConnectionSpeedOffset = 8;
    public const int ProfileFlag9Offset = 9;
    public const int ExecutableSizeTokenOffset = 0x0A;
    public const int BannerFingerprintOffset = 0x2E;
    public const int ExecutableFingerprintOffset = 0x32;
    public const int NameOffset = 0x8A;
    public const int NameBytes = 256;
    public const int ProfileOffset = 0x18A;
    public const int ProfileBytes = 556;
}
