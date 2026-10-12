namespace Sacred.Network.Admission;

/// <summary>Lossless server roster. Entry zero holds server metadata; entries 1..16 correspond to Sacred slots.</summary>
public sealed class GoldPlayerRoster
{
    private readonly byte[] _payload;

    private GoldPlayerRoster(ReadOnlySpan<byte> payload) => _payload = payload.ToArray();

    public int Count => GoldPlayerRosterLayout.EntryCount;

    /// <summary>Retains the shared 834-byte record shape; record presence and object binding are separate session state.</summary>
    public GoldPlayerData GetEntry(int index)
    {
        if ((uint)index >= GoldPlayerRosterLayout.EntryCount) throw new ArgumentOutOfRangeException(nameof(index));
        return GoldPlayerData.FromPayload(_payload.AsSpan(index * GoldPlayerRosterLayout.EntryBytes, GoldPlayerRosterLayout.EntryBytes));
    }

    public static GoldPlayerRoster FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldPlayerRosterLayout.Size)
            throw new InvalidDataException($"Gold server roster requires exactly {GoldPlayerRosterLayout.Size} payload bytes.");
        return new GoldPlayerRoster(payload);
    }

    public byte[] ToPayload() => _payload.ToArray();
}
