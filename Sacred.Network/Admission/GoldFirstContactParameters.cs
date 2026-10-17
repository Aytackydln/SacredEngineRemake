namespace Sacred.Network.Admission;

/// <summary>Native client selections, supplied explicitly; retained bytes are not normalized to invented policy.</summary>
public sealed record GoldFirstContactParameters(string Name, byte CharacterType, byte ProfileByte7,
    byte ConnectionSpeed, byte ProfileFlag9, int LobbyIdentity);
