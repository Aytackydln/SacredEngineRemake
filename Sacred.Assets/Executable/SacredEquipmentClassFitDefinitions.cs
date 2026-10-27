using System.Buffers.Binary;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Executable;

public static class SacredEquipmentClassFitDefinitions
{
    public static SacredEquipmentClassFitRules Read(byte[] image)
    {
        ReadOnlySpan<byte> mutation = [0x81, 0xE2, 0xC0, 0xFF, 0x3F, 0xFF, 0x83, 0xCA, 0x40, 0x89, 0x96, 0x1C, 1, 0, 0];
        var at = image.AsSpan().IndexOf(mutation);
        var start = at - 0x31;
        if (start < 0 || start > image.Length - 0xA3 || image.AsSpan(at + 1).IndexOf(mutation) >= 0 ||
            !image.AsSpan(start).StartsWith<byte>([0x8B, 0x4E, 0x10]) ||
            !image.AsSpan(start + 0x9D).StartsWith<byte>([0x81, 0xFF]))
            throw new InvalidDataException("Original equipment class-fit operands unavailable.");
        var resources = new HashSet<uint>();
        for (var index = 0; index < 5; index++)
        {
            var operand = start + 3 + index * 8;
            if (!image.AsSpan(operand).StartsWith<byte>([0x81, 0xF9])) throw new InvalidDataException("Unsupported class-fit resource gate.");
            resources.Add(BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(operand + 2)));
        }
        return new(resources, BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(start + 0x9F)));
    }
}
