namespace Sacred.Network.Admission;

/// <summary>Native client selections, supplied explicitly; retained bytes are not normalized to invented policy.</summary>
public sealed record GoldFirstContactParameters(string Name, byte CharacterType, byte ProfileByte7,
    byte ConnectionSpeed, byte ProfileFlag9, int LobbyIdentity)
{
    /// <summary>Native direct LAN join to a running GameServer. The first slot's
    /// profile flag must not request that GameServer lock the session.</summary>
    public static GoldFirstContactParameters ForLanGameServer(string name, byte characterType, GoldConnectionMode mode)
        => new(name, characterType, ProfileByte7: 1, ConnectionSpeed: (byte)mode, ProfileFlag9: 0, LobbyIdentity: 0);
}
