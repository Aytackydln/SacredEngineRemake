using System.Buffers.Binary;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x440560: infer native class flags from a resolved resource name.
/// Returns the native Boolean result, including the unmatched-armor false case.
/// Does not substitute localized names or presentation class masks.</summary>
public static class SacredEquipmentClassInference
{
    public static bool Apply(Span<byte> instance, SacredEquipmentClassRules rules,
        Func<uint, uint?> parentResource, Func<uint, string?> resourceName)
    {
        if (instance.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected 130-byte item values.", nameof(instance));
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(parentResource);
        ArgumentNullException.ThrowIfNull(resourceName);
        var type = BinaryPrimitives.ReadUInt32LittleEndian(instance);
        if (rules.ExcludedResources.Any(range => range.Contains(type))) return true;
        for (var link = 0; link < rules.MaximumParentLinks && rules.ParentResources.Any(range => range.Contains(type)); link++)
        {
            var parent = parentResource(type);
            if (!parent.HasValue) return true;
            type = parent.Value;
        }
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]);
        if (type == 0 || rules.PreservedResources.Contains(type) && (flags & 0x40) == 0) return true;
        var name = resourceName(type);
        if (name is null) return true;
        var armor = name.Contains(rules.ArmorMarker, StringComparison.Ordinal);
        if (armor && rules.ArmorExclusions.Any(text => name.Contains(text, StringComparison.Ordinal))) return true;
        if (!armor && !rules.JewelryMarkers.Any(text => name.Contains(text, StringComparison.Ordinal))) return true;
        flags &= rules.ClearMask;
        foreach (var rule in armor ? rules.ArmorRules : rules.JewelryRules)
            if (name.Contains(rule.Text, StringComparison.Ordinal))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(instance[4..], flags | rule.Flag);
                return true;
            }
        BinaryPrimitives.WriteUInt32LittleEndian(instance[4..], flags);
        return !armor;
    }
}
