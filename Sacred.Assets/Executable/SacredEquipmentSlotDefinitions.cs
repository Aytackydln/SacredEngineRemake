using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Original resource overrides at the beginning of Gold 0x54ACC0.</summary>
public sealed class SacredEquipmentSlotDefinitions
{
    private readonly uint _first;
    private readonly uint _second;
    private SacredEquipmentSlotDefinitions(uint first, uint second) => (_first, _second) = (first, second);
    public bool ForcesLeftHand(uint type) => type == _first || type == _second;

    public static SacredEquipmentSlotDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> compare = [0x81, 0xFF];
        ReadOnlySpan<byte> branch = [0x0F, 0x84];
        var found = -1;
        ReadOnlySpan<byte> predicate = [0x57, 0xE8];
        ReadOnlySpan<byte> predicateBranch = [0x84, 0xC0, 0x0F, 0x85];
        for (var i = 0; i <= image.Length - 44; i++)
        {
            if (!image.Slice(i, 2).SequenceEqual(compare) || !image.Slice(i + 6, 2).SequenceEqual(branch) ||
                !image.Slice(i + 12, 2).SequenceEqual(compare) || !image.Slice(i + 18, 2).SequenceEqual(branch) ||
                image[i + 24] != 0x8B || image[i + 25] != 0x0D ||
                !image.Slice(i + 30, predicate.Length).SequenceEqual(predicate) ||
                !image.Slice(i + 36, predicateBranch.Length).SequenceEqual(predicateBranch)) continue;
            if (12L + BinaryPrimitives.ReadInt32LittleEndian(image[(i + 8)..]) !=
                24L + BinaryPrimitives.ReadInt32LittleEndian(image[(i + 20)..])) continue;
            if (12L + BinaryPrimitives.ReadInt32LittleEndian(image[(i + 8)..]) !=
                44L + BinaryPrimitives.ReadInt32LittleEndian(image[(i + 40)..])) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original equipment slot overrides.");
            found = i;
        }
        if (found < 0) throw new InvalidDataException("Original equipment slot overrides unavailable.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(image[(found + 2)..]), BinaryPrimitives.ReadUInt32LittleEndian(image[(found + 14)..]));
    }

    public static SacredEquipmentSlotDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment slot definitions unavailable.", failure);
    }
}
