using System.Buffers.Binary;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5BEB30(-1) normalizes the own sWeaponInfo before returning
/// it. The four bounds are compared as signed words, including overflowed damage.</summary>
public static class SacredEquipmentDamageBounds
{
    public static void Normalize(Span<byte> instance)
    {
        if (instance.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("Expected one 130-byte item record.", nameof(instance));
        for (var element = 0; element < 4; element++)
        {
            var lower = 28 + element * 2;
            var upper = 36 + element * 2;
            var minimum = BinaryPrimitives.ReadInt16LittleEndian(instance[lower..]);
            var maximum = BinaryPrimitives.ReadInt16LittleEndian(instance[upper..]);
            if (minimum <= maximum) continue;
            BinaryPrimitives.WriteInt16LittleEndian(instance[lower..], maximum);
            BinaryPrimitives.WriteInt16LittleEndian(instance[upper..], minimum);
        }
    }
}
