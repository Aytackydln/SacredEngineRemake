using System.Buffers.Binary;

namespace Sacred.Network.Admission;

/// <summary>The 25 ordered comparisons at GameServer 0x4EB581..0x4EB79C.</summary>
public sealed class GoldDataFingerprints
{
    public const int Count = 25;
    private readonly uint[] _values;

    public static IReadOnlyList<GoldDataFingerprintField> Fields { get; } = Array.AsReadOnly(CreateFields());

    public GoldDataFingerprints(ReadOnlySpan<uint> values)
    {
        if (values.Length != Count) throw new ArgumentException("Exactly 25 Gold fingerprints required.", nameof(values));
        _values = values.ToArray();
    }

    public uint this[int index] => _values[index];

    /// <summary>Apply Gold's verified loader rules to independently computed file checksums.</summary>
    public static GoldDataFingerprints FromFileChecksums(ReadOnlySpan<uint> values)
    {
        var result = new GoldDataFingerprints(values);
        for (var index = 3; index <= 18; index++)
            if (index != 6 && result._values[index] == 0) result._values[index] = uint.MaxValue;
        // Native clears Items13 only after the Creature/Weapon/Items..Items12 checks succeed.
        if (!result._values.AsSpan(0, 16).Contains(0u)) result._values[16] = 0;
        return result;
    }

    public uint GetMismatchMask(GoldDataFingerprints expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        uint mask = 0;
        for (var index = 0; index < Count; index++)
            if (_values[index] != expected[index]) mask |= Fields[index].MismatchMask;
        return mask;
    }

    internal static GoldDataFingerprints Read(ReadOnlySpan<byte> payload)
    {
        Span<uint> values = stackalloc uint[Count];
        for (var index = 0; index < Count; index++)
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(payload[Fields[index].PayloadOffset..]);
        return new GoldDataFingerprints(values);
    }

    internal void Write(Span<byte> payload)
    {
        for (var index = 0; index < Count; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(payload[Fields[index].PayloadOffset..], _values[index]);
    }

    private static GoldDataFingerprintField[] CreateFields()
    {
        var fields = new GoldDataFingerprintField[Count];
        fields[0] = new(0x0E, 1, "Pak/Creature.pak");
        fields[1] = new(0x12, 2, "Pak/Weapon.pak");
        fields[2] = new(0x36, 4, "Pak/Items.pak");
        for (var number = 0; number < 16; number++)
        {
            var index = number + 3;
            fields[index] = new(0x3A + number * 4, 1u << index, $"Pak/Items{number:00}.pak");
        }
        fields[19] = new(0x16, 1u << 19, "Bin/Balance.bin");
        fields[20] = new(0x1A, 1u << 20, "Bin/wpmod.bin");
        fields[21] = new(0x1E, 1u << 21, "Bin/World.bin");
        fields[22] = new(0x22, 1u << 22, "Bin/World2.bin");
        fields[23] = new(0x26, 1u << 23, "Bin/Wea.bin");
        fields[24] = new(0x2A, 1u << 24, "World/sectors.keyx");
        return fields;
    }
}
