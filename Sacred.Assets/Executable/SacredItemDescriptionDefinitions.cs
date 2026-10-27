using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Original extra base-resource comparisons in cWeapon3D::isTwoHanded (pureHD 0x5CF110).</summary>
public static class SacredItemDescriptionDefinitions
{
    public static IReadOnlyList<uint> ReadTwoHandedBases(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> signature = [0x3C, 0x0C, 0x74, 0x34, 0x3C, 0x04, 0x74, 0x30, 0x81, 0xFE];
        var start = image.IndexOf(signature);
        if (start < 0 || image[(start + 1)..].IndexOf(signature) >= 0 || start + 56 > image.Length)
            throw new InvalidDataException("Original two-handed description rules unavailable or ambiguous.");
        var result = new uint[6];
        for (var index = 0; index < result.Length; index++)
        {
            var at = start + 8 + index * 8;
            if (image[at] != 0x81 || image[at + 1] != 0xFE || image[at + 6] is not (0x74 or 0x75))
                throw new InvalidDataException("Unsupported two-handed resource comparison.");
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(image[(at + 2)..]);
        }
        return Array.AsReadOnly(result);
    }

    public static IReadOnlyList<uint> LoadTwoHandedBases(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return ReadTwoHandedBases(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original item description rules unavailable.", failure);
    }
}
