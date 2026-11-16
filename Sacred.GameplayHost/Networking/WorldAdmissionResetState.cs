namespace Sacred.GameplayHost.Networking;

/// <summary>Compared byte-zero entry state of the 0x417E70 singleton. Its other
/// graphics/resource fields and their construction remain the factory's concern.</summary>
public sealed class WorldAdmissionResetState(byte entryFlag0)
{
    public byte EntryFlag0 { get; private set; } = entryFlag0;
    internal void ResetEntry() => EntryFlag0 = 0;
}
