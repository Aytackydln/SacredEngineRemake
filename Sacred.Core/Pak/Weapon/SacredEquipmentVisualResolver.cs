using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Native Weapon.pak base-visual copying (0x425A35), in equipment record order.</summary>
public static class SacredEquipmentVisualResolver
{
    public static IReadOnlyList<SacredEquipment> Resolve(
        IReadOnlyDictionary<ushort, ItemsPakEntry> items, IReadOnlyList<SacredEquipment> equipment)
    {
        var visuals = items.ToDictionary(pair => pair.Key, pair => pair.Value);
        var resolved = new SacredEquipment[equipment.Count];
        for (var index = 0; index < equipment.Count; index++)
        {
            var entry = equipment[index];
            var visual = entry.Item;
            if (entry.BaseItemId is > 0 and <= ushort.MaxValue &&
                visuals.TryGetValue((ushort)entry.BaseItemId, out var basis))
            {
                visual = visual with
                {
                    ModelDesc = basis.ModelDesc.WithItemId(visual.ModelDesc.ItemId != 0 ? visual.ModelDesc.ItemId : entry.IdemId),
                    ModelName = basis.ModelName
                };
                visuals[visual.ItemIndex] = visual;
            }
            resolved[index] = entry with { Item = visual };
        }
        return resolved;
    }
}
