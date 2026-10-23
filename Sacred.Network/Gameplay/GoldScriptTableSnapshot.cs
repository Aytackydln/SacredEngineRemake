namespace Sacred.Network.Gameplay;

/// <summary>One native bulk replacement, including bytes copied past the logical record boundary.</summary>
public sealed class GoldScriptTableSnapshot
{
    private readonly byte[] _payload;
    private GoldScriptTableSnapshot(ushort opcode, byte[] payload)
    {
        Opcode = opcode; RecordSize = GoldScriptTableLayout.GetRecordSize(opcode); _payload = payload;
    }
    public ushort Opcode { get; }
    public int RecordSize { get; }
    public int Count => _payload.Length / RecordSize;
    /// <summary>Native resizes to floor(length/stride) then copies the entire payload. Retain the
    /// remainder as transfer evidence without interpreting it as a complete record.</summary>
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(Count * RecordSize);
    public ReadOnlySpan<byte> GetRecord(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _payload.AsSpan(index * RecordSize, RecordSize);
    }
    public byte[] ToPayload() => _payload.ToArray();
    public static GoldScriptTableSnapshot FromPayload(ushort opcode, ReadOnlySpan<byte> payload)
    {
        GoldScriptTableLayout.GetRecordSize(opcode);
        return new(opcode, payload.ToArray());
    }
}
