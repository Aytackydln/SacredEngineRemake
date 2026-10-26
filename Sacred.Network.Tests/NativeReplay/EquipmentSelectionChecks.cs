using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

internal static class EquipmentSelectionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-selection.json")));
        var pool = fixture.RootElement.GetProperty("candidates").EnumerateArray().Select(row =>
            new SacredEquipmentSelectionCandidate(row.GetProperty("item_type").GetUInt32(), row.GetProperty("usage").GetByte(),
                row.GetProperty("set_type").GetByte(), MemoryMarshal.Read<SacredEquipmentInstanceLayout>(Convert.FromHexString(row.GetProperty("values_hex").GetString()!)),
                row.GetProperty("modifier_hex").ValueKind == JsonValueKind.Null ? null :
                    MemoryMarshal.Read<SacredWeaponModifierLayout>(Convert.FromHexString(row.GetProperty("modifier_hex").GetString()!)))).ToArray();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var criteria = new SacredEquipmentSelectionCriteria(row.GetProperty("level").GetUInt16(), row.GetProperty("usage").GetByte(),
                row.GetProperty("hero_class").GetUInt32(), row.GetProperty("flags").GetByte(), row.GetProperty("exclude_sets").GetBoolean(), row.GetProperty("limit").GetUInt32());
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var result = SacredEquipmentSelectionFilter.Apply(pool, (SacredItemCategory)row.GetProperty("category").GetByte(), criteria, random);
            Check(result.Select(candidate => candidate.ItemType).SequenceEqual(row.GetProperty("result").EnumerateArray().Select(value => value.GetUInt32())),
                $"Native equipment candidate filter differs: {row}; actual {string.Join(',', result.Select(candidate => candidate.ItemType))}");
            Check(random.State == row.GetProperty("filtered_seed").GetUInt32(), $"Native equipment filter RNG differs: {row}");
            if (row.GetProperty("final_seed").ValueKind != JsonValueKind.Null)
            {
                var selected = SacredEquipmentSelectionDraw.ChooseOffline(result, criteria.HeroClass, random);
                Check((selected ?? 0) == row.GetProperty("selected").GetUInt32(), $"Native offline equipment draw differs: {row}");
                Check(random.State == row.GetProperty("final_seed").GetUInt32(), $"Native equipment draw RNG differs: {row}");
            }
        }
        var drawPool = fixture.RootElement.GetProperty("draw_candidates").EnumerateArray().Select(row =>
            new SacredEquipmentSelectionCandidate(row.GetProperty("item_type").GetUInt32(), 0, 0,
                MemoryMarshal.Read<SacredEquipmentInstanceLayout>(Convert.FromHexString(row.GetProperty("values_hex").GetString()!)), null)).ToArray();
        foreach (var row in fixture.RootElement.GetProperty("draw_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var result = SacredEquipmentSelectionDraw.ChooseOffline(drawPool.Take(row.GetProperty("count").GetInt32()).ToArray(),
                row.GetProperty("hero_class").GetUInt32(), random);
            Check((result ?? 0) == row.GetProperty("selected").GetUInt32(), $"Native equipment draw boundary differs: {row}");
            Check(random.State == row.GetProperty("final_seed").GetUInt32(), $"Native equipment draw boundary RNG differs: {row}");
        }
        return checks;
    }
}
