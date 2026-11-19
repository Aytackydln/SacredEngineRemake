using Sacred.Network.Admission;

namespace Sacred.Network.Gameplay;

/// <summary>Known 834-byte roster rows; clearing a remote slot does not fabricate other missing rows.</summary>
public sealed class GoldSessionRosterReplica
{
    private readonly Dictionary<byte, GoldPlayerData> _rows = [];
    public GoldPlayerData? Find(byte slot) => _rows.GetValueOrDefault(slot);
    public string? Name(byte slot) => Find(slot)?.Name;
    /// <summary>Gold 7DB2C0 counts nonzero IPv4 words in slots 1..16, not
    /// presence bytes or character types. Unknown until the complete roster is
    /// observed; callers must also establish the actual connection-active gate.</summary>
    public uint? NativePlayerCount => _rows.Count == GoldPlayerRosterLayout.EntryCount
        ? (uint)Enumerable.Range(1, 16).Count(slot => _rows[(byte)slot].NetworkAddressWord0 != 0)
        : null;
    internal void Apply(GoldPlayerRosterEvent incoming)
    {
        var roster = (GoldPlayerRosterEvent)GoldGameEventDecoder.Decode(incoming.Message);
        _rows.Clear();
        for (byte i = 0; i <= 16; i++) _rows.Add(i, roster.Roster.GetEntry(i));
    }
    internal void ClearPlayer(byte slot) { if (slot <= 16) _rows[slot] = GoldPlayerData.FromPayload(new byte[834]); }
    internal void SetProfile(byte slot, ReadOnlySpan<byte> profile)
    {
        if (!_rows.TryGetValue(slot, out var row)) return;
        var bytes = row.ToPayload(); profile.CopyTo(bytes.AsSpan(GoldPlayerDataLayout.ProfileOffset));
        _rows[slot] = GoldPlayerData.FromPayload(bytes);
    }
    internal void Clear() => _rows.Clear();
}
