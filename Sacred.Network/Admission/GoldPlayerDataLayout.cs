namespace Sacred.Network.Admission;

/// <summary>Gold opcode 0x1B: exact size checked by GameServer 0x4D2EE0.</summary>
public static class GoldPlayerDataLayout
{
    public const ushort Opcode = 0x1B;
    public const int Size = 834;
    /// <summary>Admission copies the word returned from native 0x182ECF0 +0x14.
    /// The compared session-import IP selector establishes this as its selected
    /// IPv4 word; it is not the admission controller's player-slot field.</summary>
    public const int SessionWord0Offset = 0;
    /// <summary>Gold 7DB2C0 counts nonzero words here in roster slots 1..16
    /// while connected. Record zero and the presence byte are excluded.</summary>
    public const int NetworkAddressWord0Offset = SessionWord0Offset;
    public const int CharacterTypeOffset = 4;
    public const int ProfileByte5Offset = 5;
    /// <summary>Native roster consumer snapshots/tests this byte for player
    /// presence and new-remote-player notices; independent of character type.</summary>
    public const int PresenceByteOffset = 6;
    /// <summary>UInt32 setting, copied to native roster +0x60 (record base +0x54). Only 1 is low bandwidth.</summary>
    public const int ConnectionSpeedOffset = 0x0C;
    public const int VersionOffset = 0x10;
    public const int LobbyIdentityOffset = 0x12;
    public const int NameOffset = 0x16;
    public const int NameBytes = 256;
    public const int ProfileOffset = 0x116;
    public const int ProfileBytes = 556;
}
