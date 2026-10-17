using System.Buffers.Binary;

namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Bounded current-format object directory; category resolution comes from the original game archives.</summary>
public sealed class SacredSavedObjectList
{
    private readonly byte[] _payload;
    private readonly SacredSavedObjectRecord[] _records;
    private SacredSavedObjectList(byte[] payload, SacredSavedObjectRecord[] records, int size) => (_payload, _records, SerializedSize) = (payload, records, size);
    public int Count => _records.Length;
    public int SerializedSize { get; }
    public ReadOnlySpan<SacredSavedObjectRecord> Records => _records;
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(SerializedSize);
    public byte[] ToPayload() => _payload.ToArray();
    public static SacredSavedObjectList FromPayload(ReadOnlySpan<byte> payload, byte version, Func<uint, byte> resolveCategory, int maximumRecords)
    {
        ArgumentNullException.ThrowIfNull(resolveCategory);
        if (maximumRecords < 0) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        SacredSavedStreamBounds.RequireVersion(version);
        SacredSavedStreamBounds.Require(payload, 4);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (count > maximumRecords || count > (payload.Length - 4) / 4) throw new InvalidDataException("Saved object count exceeds input bounds or the caller's record budget.");
        var records = new SacredSavedObjectRecord[(int)count];
        var position = 4;
        for (var index = 0; index < records.Length; index++)
        {
            var data = SacredSavedStreamBounds.Advance(payload, position, 1, 4);
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(payload[position..]);
            if (resource == 0) records[index] = new(0, 0, null, null);
            else
            {
                var category = resolveCategory(resource);
                records[index] = category switch
                {
                    2 or 3 or 32 => new(resource, category, null, SacredSavedCreature.FromPayload(payload[data..], version)),
                    5 or 33 => new(resource, category, SacredSavedItem.FromPayload(payload[data..], version, true), null),
                    6 or 7 or 8 or 9 or 13 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 26 or 28 or 29 or 30 or 31
                        => new(resource, category, SacredSavedItem.FromPayload(payload[data..], version, false), null),
                    _ => throw new NotSupportedException($"Saved object category {category} has no verified reader.")
                };
            }
            position += records[index].SerializedSize;
        }
        return new(payload.ToArray(), records, position);
    }
}
