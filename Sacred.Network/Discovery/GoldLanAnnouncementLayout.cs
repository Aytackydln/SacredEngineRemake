namespace Sacred.Network.Discovery;

/// <summary>Gold GameServer's UDP announcement at network state +0x596C (writer 0x4E99A0).</summary>
public static class GoldLanAnnouncementLayout
{
    public const int HeaderBytes = 4;
    public const uint RawFlag = 0x08000000;
    public const int PayloadBytes = 0xAE;
    public const int VersionOffset = 0;
    public const int TcpPortOffset = 2;
    public const int AddressOffset = 4; // UInt32 in host order, unlike sockaddr's network-order address.
    public const int FlagsOffset = 8;
    // Seeds selected connection +40424 (Gold 7D3DCD); includes difficulty bits 100/200/400/800.
    public const int PlayersOffset = 12;
    public const int MaximumPlayersOffset = 13;
    public const int NameOffset = 14;
    public const int NameCharacters = 80; // UTF-16LE; all 80 characters may be occupied.
}
