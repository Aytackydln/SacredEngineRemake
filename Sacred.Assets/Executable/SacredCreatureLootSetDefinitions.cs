using System.Buffers.Binary;
using Sacred.Core.Loot;

namespace Sacred.Assets.Executable;

/// <summary>Original selector-one preferred-set exclusion operands.</summary>
public static class SacredCreatureLootSetDefinitions
{
    public static SacredCreatureLootSetRules Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x8B, 0x46, 0x10, 0x33, 0xFF, 0x48, 0x89, 0x7C, 0x24, 0x30,
            0x83, 0xF8, 8, 0xC6, 0x44, 0x24, 0x36, 0, 0xC7, 0x44, 0x24, 0x14];
        var start = image.AsSpan().IndexOf(signature);
        if (start < 0 || start > image.Length - 0x18A || image.AsSpan(start + 1).IndexOf(signature) >= 0 ||
            !image.AsSpan(start + 0x176).StartsWith<byte>([0x81, 0xFE]) ||
            !image.AsSpan(start + 0x17E).StartsWith<byte>([0x81, 0xFE]))
            throw new InvalidDataException("Original creature set-selection operands unavailable.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(start + 0x178)),
            BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(start + 0x180)));
    }
}
