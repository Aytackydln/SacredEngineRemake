using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

public readonly record struct SacredEquipmentPriceOverrides(bool ClearClassRestrictions, bool ClearLevelAndSkillRequirements);

/// <summary>Gold pricing's resource-specific requirement overrides, extracted from
/// original instructions. The generic calculator has no hardcoded item IDs.</summary>
public sealed class SacredEquipmentPriceDefinitions
{
    private readonly uint _clearClass;
    private readonly uint _firstClearRequirements;
    private readonly uint _secondClearRequirements;
    private SacredEquipmentPriceDefinitions(uint clearClass, uint first, uint second)
        => (_clearClass, _firstClearRequirements, _secondClearRequirements) = (clearClass, first, second);
    public SacredEquipmentPriceOverrides Overrides(uint type) => new(type == _clearClass, type == _firstClearRequirements || type == _secondClearRequirements);

    public static SacredEquipmentPriceDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> classStore = [0x75, 4, 0x83, 0x65, 4, 0xC0, 0x3D];
        ReadOnlySpan<byte> branches = [0x74, 7, 0x3D];
        ReadOnlySpan<byte> stores = [0x75, 0x18, 0xC6, 0x45, 0x14, 0, 0xC6, 0x45, 0x1A, 0,
            0xC6, 0x45, 0x15, 0, 0xC6, 0x45, 0x18, 0, 0xC6, 0x45, 0x16, 0, 0xC6, 0x45, 0x17, 0];
        var found = -1;
        for (var i = 0; i <= image.Length - 49; i++)
        {
            if (image[i] != 0x3D || !image.Slice(i + 5, classStore.Length).SequenceEqual(classStore) ||
                !image.Slice(i + 16, branches.Length).SequenceEqual(branches) ||
                !image.Slice(i + 23, stores.Length).SequenceEqual(stores)) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original equipment price overrides.");
            found = i;
        }
        if (found < 0) throw new InvalidDataException("Original equipment price overrides unavailable.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(image[(found + 1)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(image[(found + 12)..]), BinaryPrimitives.ReadUInt32LittleEndian(image[(found + 19)..]));
    }

    public static SacredEquipmentPriceDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment price definitions unavailable.", failure);
    }
}
