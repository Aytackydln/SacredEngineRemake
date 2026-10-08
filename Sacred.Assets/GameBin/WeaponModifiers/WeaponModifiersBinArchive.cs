using System.Buffers.Binary;
using Sacred.Core.GameBin.WeaponModifiers;

namespace Sacred.Assets.GameBin.WeaponModifiers;

public static class WeaponModifiersBinArchive
{
    public static IReadOnlyList<SacredWeaponModifier> Load(string path) => Parse(File.ReadAllBytes(path));

    public static IReadOnlyList<SacredWeaponModifier> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
            throw new InvalidDataException("wpmod.bin has no record count.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count > (data.Length - sizeof(uint)) / SacredWeaponModifierRecordLayout.Size)
            throw new InvalidDataException("wpmod.bin record count exceeds the file.");

        var records = new SacredWeaponModifier[(int)count];
        var offset = sizeof(uint);
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = WeaponModifierRecordReader.Read(data[offset..], index, offset, out var bytesRead);
            offset += bytesRead;
        }

        if (offset != data.Length)
            throw new InvalidDataException("Unexpected trailing bytes in wpmod.bin.");

        return records;
    }
}
