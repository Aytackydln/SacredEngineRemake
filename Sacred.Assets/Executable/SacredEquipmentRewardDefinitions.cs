using System.Buffers.Binary;
using Sacred.Core.Loot;

namespace Sacred.Assets.Executable;

/// <summary>Reads the reward wrapper's original money resource and call counts.</summary>
public static class SacredEquipmentRewardDefinitions
{
    public static SacredEquipmentRewardRules Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x8B, 0xE9, 0xBF, 8, 0, 0, 0, 0x89, 0x6C, 0x24, 0x18,
            0x8B, 0xF7, 0x33, 0xDB, 0x53, 0x53, 0x53, 0x68];
        var at = image.AsSpan().IndexOf(signature);
        if (at < 0 || at > image.Length - 0x50 || image.AsSpan(at + 1).IndexOf(signature) >= 0 ||
            !image.AsSpan(at + 0x48).StartsWith<byte>([0xC7, 0x44, 0x24, 0x28]))
            throw new InvalidDataException("Original equipment reward wrapper unavailable or unsupported.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 19)), image[at + 3],
            checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 0x4C))));
    }
}
