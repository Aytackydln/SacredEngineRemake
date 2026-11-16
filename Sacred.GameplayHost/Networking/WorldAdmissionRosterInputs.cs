namespace Sacred.GameplayHost.Networking;

/// <summary>Authoritative native getters/fields at roster construction. Raw
/// UTF16 storage is copied without replacement/termination normalization.</summary>
public sealed class WorldAdmissionRosterInputs(uint sessionWord0, byte characterType, byte profileByte5,
    uint connectionFlags, int alternateLobbyIdentity, ReadOnlyMemory<byte> localName, ReadOnlyMemory<byte> replacementName)
{
    public uint SessionWord0 { get; } = sessionWord0;
    /// <summary>Compared admission source: address-manager 0x7F5F60 selected IPv4
    /// word. SessionWord0 remains the compatibility name for the same raw bits.</summary>
    public uint NetworkAddressWord0 => SessionWord0;
    public byte CharacterType { get; } = characterType;
    public byte ProfileByte5 { get; } = profileByte5;
    public uint ConnectionFlags { get; } = connectionFlags;
    public int AlternateLobbyIdentity { get; } = alternateLobbyIdentity;
    public ReadOnlyMemory<byte> LocalName { get; } = localName.ToArray();
    public ReadOnlyMemory<byte> ReplacementName { get; } = replacementName.ToArray();
}
