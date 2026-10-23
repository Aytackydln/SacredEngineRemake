namespace Sacred.Network.Gameplay;

/// <summary>Gold TRIGGER_INSERT_SPAWNRENEW record; its scheduling fields remain unresolved.</summary>
public sealed class GoldSpawnRenewRecord
{
    private readonly byte[] _bytes;
    private GoldSpawnRenewRecord(byte[] bytes) => _bytes = bytes;
    public byte[] ToPayload() => _bytes.ToArray();
    public static GoldSpawnRenewRecord FromPayload(ReadOnlySpan<byte> data) => data.Length == GoldScriptRegistrationLayout.SpawnRenewSize
        ? new(data.ToArray()) : throw new InvalidDataException("Gold spawn-renew record requires 60 bytes.");
}
