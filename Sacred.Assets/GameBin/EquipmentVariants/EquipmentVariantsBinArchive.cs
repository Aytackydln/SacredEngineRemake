using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.EquipmentVariants;

namespace Sacred.Assets.GameBin.EquipmentVariants;

public static class EquipmentVariantsBinArchive
{
    public static IReadOnlyList<IReadOnlyList<SacredEquipmentVariantLayout>> Load(string path) => Read(File.ReadAllBytes(path));

    public static IReadOnlyList<IReadOnlyList<SacredEquipmentVariantLayout>> Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Rust.bin has no profile count.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (count > (file.Length - 4) / 4) throw new InvalidDataException("Rust.bin profile count exceeds the file.");
        var result = new IReadOnlyList<SacredEquipmentVariantLayout>[(int)count];
        var offset = 4;
        for (var i = 0; i < result.Length; i++)
        {
            if (offset > file.Length - 4) throw new InvalidDataException("Truncated Rust.bin profile count.");
            var rows = BinaryPrimitives.ReadUInt32LittleEndian(file[offset..]);
            offset += 4;
            if (rows > (file.Length - offset) / SacredEquipmentVariantLayout.Size) throw new InvalidDataException("Truncated Rust.bin profile rows.");
            var length = checked((int)rows * SacredEquipmentVariantLayout.Size);
            result[i] = MemoryMarshal.Cast<byte, SacredEquipmentVariantLayout>(file.Slice(offset, length)).ToArray();
            offset += length;
        }
        if (offset != file.Length) throw new InvalidDataException("Unexpected trailing bytes in Rust.bin.");
        return result;
    }
}
