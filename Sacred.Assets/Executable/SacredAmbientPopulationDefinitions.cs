using System.Buffers.Binary;
using System.Collections.Frozen;

namespace Sacred.Assets.Executable;

/// <summary>Resources and exceptional raw regions selected by Gold's ambient scan.
/// The installed instruction immediates supply every resource/region identity.</summary>
public sealed class SacredAmbientPopulationDefinitions
{
    private readonly FrozenSet<byte> _regions;
    private SacredAmbientPopulationDefinitions(uint first, uint second, uint special, byte[] regions)
        => (FirstType, SecondType, SpecialType, _regions) = (first, second, special, regions.ToFrozenSet());
    public uint FirstType { get; }
    public uint SecondType { get; }
    public uint SpecialType { get; }
    public bool UsesSpecialType(byte region) => _regions.Contains(region);
    public static SacredAmbientPopulationDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original ambient population selectors unavailable.", failure);
    }
    public static SacredAmbientPopulationDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> choice = [0x99, 0xB9, 3, 0, 0, 0, 0xF7, 0xF9, 0x3B, 0xD3, 0x75, 7, 0xBE];
        ReadOnlySpan<byte> regionPrefix = [0x66, 0x83, 0xFF];
        for (var offset = 55; offset < image.Length - 64; offset++)
        {
            ReadOnlySpan<byte> code = image.AsSpan(offset, 64);
            if (!code[..choice.Length].SequenceEqual(choice) || code[17] != 0xEB || code[18] != 10 ||
                code[19] != 0xE8 || code[24] != 0xBE || code[57] != 0xBE ||
                !image.AsSpan(offset - 55, 3).SequenceEqual(regionPrefix)) continue;
            var regions = new byte[4];
            regions[0] = image[offset - 52];
            var valid = true;
            for (var index = 1; index < 4; index++)
            {
                var start = offset - 45 + (index - 1) * 6;
                valid &= image.AsSpan(start, 3).SequenceEqual(regionPrefix) && image[start + 4] == 0x74;
                regions[index] = image[start + 3];
            }
            if (!valid) continue;
            var first = BinaryPrimitives.ReadUInt32LittleEndian(code[13..]);
            var second = BinaryPrimitives.ReadUInt32LittleEndian(code[25..]);
            var special = BinaryPrimitives.ReadUInt32LittleEndian(code[58..]);
            if (first == 0 || second == 0 || special == 0 || first == second || first == special || second == special)
                throw new InvalidDataException("Invalid original ambient resources.");
            return new(first, second, special, regions);
        }
        throw new InvalidDataException("Original ambient population selectors not found.");
    }
}
