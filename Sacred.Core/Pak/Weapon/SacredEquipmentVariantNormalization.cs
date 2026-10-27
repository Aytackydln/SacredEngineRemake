using System.Buffers.Binary;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5C7370 after shared lookup. Replacing the physical/value
/// resource and resetting its model precedes the concrete class restriction.
/// No replacement is a false return even if the existing classes already fit.</summary>
public static class SacredEquipmentVariantNormalization
{
    public static bool Apply(Span<byte> ownValues, uint physicalResource, uint actorResource, byte? sharedSetType,
        Func<SacredEquipmentVariantResult> resolve, Action<uint> replaceAndResetModel,
        Func<uint?> currentClassFlags)
    {
        if (ownValues.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected concrete equipment values.", nameof(ownValues));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(ownValues[4..]);
        if (!sharedSetType.HasValue || (flags & 0x30000000) == 0 && sharedSetType.Value != 0) return false;
        var variant = resolve();
        if (!variant.Supported || variant.ReplacementType == 0 || variant.ReplacementType == physicalResource)
        {
            _ = currentClassFlags();
            return false;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(ownValues, variant.ReplacementType);
        replaceAndResetModel(variant.ReplacementType);
        var effectiveFlags = currentClassFlags();
        if (!effectiveFlags.HasValue) return true;
        const uint classes = 0xC0003F;
        var restriction = effectiveFlags.Value & classes;
        if (restriction is 0 or classes || actorResource is < 1 or > 9) return true;
        var mask = actorResource switch { <= 6 => 1u << ((int)actorResource - 1), 7 => 0x20u, 8 => 0x400000u, _ => 0x800000u };
        if ((effectiveFlags.Value & mask) == 0)
            BinaryPrimitives.WriteUInt32LittleEndian(ownValues[4..], (BinaryPrimitives.ReadUInt32LittleEndian(ownValues[4..]) & ~classes) | mask);
        return true;
    }
}
