namespace Sacred.GameplayHost.Networking;

/// <summary>Owned arguments captured before the native profile-message service.
/// The profile copy precedes the name copy; later callbacks cannot change it.</summary>
public sealed class WorldRosterProfileRequest(byte slot, string name, ReadOnlyMemory<byte> profile)
{
    public byte Slot { get; } = slot;
    public string Name { get; } = name;
    public ReadOnlyMemory<byte> Profile { get; } = profile.ToArray();
}
