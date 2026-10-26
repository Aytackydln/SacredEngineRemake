using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

internal static class EquipmentSelectionPoolChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        using var lookup = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-modifier-lookup.json")));
        var definitions = SacredEquipmentSelectionDefinitions.Read(Convert.FromHexString(lookup.RootElement.GetProperty("selection_code_hex").GetString()!));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-selection-pool.json")));
        var entries = fixture.RootElement.GetProperty("candidates").EnumerateArray().Select(row =>
            new SacredEquipmentSelectionPoolEntry(MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Convert.FromHexString(row.GetProperty("descriptor_hex").GetString()!)),
                new(row.GetProperty("item_type").GetUInt32(), 0, 0,
                    MemoryMarshal.Read<SacredEquipmentInstanceLayout>(Convert.FromHexString(row.GetProperty("values_hex").GetString()!)),
                    row.GetProperty("modifier_hex").ValueKind == JsonValueKind.Null ? null :
                        MemoryMarshal.Read<SacredWeaponModifierLayout>(Convert.FromHexString(row.GetProperty("modifier_hex").GetString()!))))).Reverse().ToArray();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pool = SacredEquipmentSelectionPool.Build(entries, (SacredItemCategory)row.GetProperty("category").GetByte(), definitions.PoolRules);
            if (!pool.Select(candidate => candidate.ItemType).SequenceEqual(row.GetProperty("result").EnumerateArray().Select(value => value.GetUInt32())))
                throw new Exception($"Native category pool differs: {row}; actual {string.Join(',', pool.Select(candidate => candidate.ItemType))}");
            checks++;
        }
        return checks;
    }
}
