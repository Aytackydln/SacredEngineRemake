using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>585260's saved-component profile normalization. Operand-derived
/// type cases keep native resource exceptions out of generic runtime rules.</summary>
public sealed class SacredSavedCreatureProfiles
{
    private readonly Dictionary<uint, byte> _profiles;
    private readonly uint _lastType;
    private readonly byte _base;
    private SacredSavedCreatureProfiles(Dictionary<uint, byte> profiles, uint lastType, byte basis)
        => (_profiles, _lastType, _base) = (profiles, lastType, basis);
    public byte Profile(uint resource) => _profiles.TryGetValue(resource, out var profile) ? profile : checked((byte)(_base + (resource == _lastType ? 0 : 1)));

    public static SacredSavedCreatureProfiles Read(byte[] image)
    {
        ReadOnlySpan<byte> first = [0x8B,0x4B,8,0x83,0xF9];
        var found = -1;
        for (var at = 0; at < image.Length - 80; at++)
            if (image.AsSpan(at).StartsWith(first) && image.AsSpan(at + 68, 6).SequenceEqual<byte>([0x88,0x83,0x26,1,0,0]))
            {
                if (found >= 0) throw new InvalidDataException("Ambiguous saved creature profile normalizer.");
                found = at;
            }
        if (found < 0) throw new InvalidDataException("Original saved creature profile normalizer unavailable.");
        var profiles = new Dictionary<uint, byte>();
        var cursor = found + 3;
        while (image.AsSpan(cursor).StartsWith<byte>([0x83,0xF9]))
        {
            var type = unchecked((uint)(sbyte)image[cursor + 2]);
            if (image[cursor + 3] != 0x75) throw new InvalidDataException("Unknown original profile branch.");
            var target = cursor + 5 + (sbyte)image[cursor + 4];
            var body = image.AsSpan(cursor + 5);
            byte profile;
            if (body.StartsWith<byte>([0x33,0xC0])) profile = 0;
            else if (body.StartsWith<byte>([0x8B,0xC1])) profile = checked((byte)type);
            else if (body[0] == 0xB8) profile = checked((byte)BinaryPrimitives.ReadUInt32LittleEndian(body[1..]));
            else throw new InvalidDataException("Unknown original profile assignment.");
            profiles.Add(type, profile); cursor = target;
        }
        var ending = image.AsSpan(cursor);
        if (!ending.StartsWith<byte>([0x33,0xC0,0x83,0xF9]) || !ending.Slice(5, 5).SequenceEqual<byte>([0x0F,0x95,0xC0,0x83,0xC0]))
            throw new InvalidDataException("Unknown original profile default.");
        return new(profiles, unchecked((uint)(sbyte)ending[4]), ending[10]);
    }
}
