using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Bonuses;
using Sacred.Core.Runtime;

internal static class LootRarityChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-loot-rarity.json")));
        var json = fixture.RootElement;
        var bytes = Convert.FromHexString(json.GetProperty("balance_hex").GetString()!);
        var balance = LootBalanceBinReader.Read(bytes);
        // The fixture stops after the rarity map; the full equipment tuning
        // reader also needs later magic-budget maps.
        using var ordinary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var tuning = EquipmentGenerationTuningBinReader.Read(Convert.FromHexString(ordinary.RootElement.GetProperty("balance_hex").GetString()!));
        var weights = tuning.Magic!.RarityWeights;
        var checks = 0;
        foreach (var row in json.GetProperty("chance_cases").EnumerateArray())
        {
            var actual = SacredCreatureLootChance.Calculate(balance, row.GetProperty("party").GetUInt32(),
                row.GetProperty("boosted").GetBoolean(), row.GetProperty("survival").GetSingle());
            if (actual != row.GetProperty("chance").GetInt32()) throw new Exception($"Native initial loot budget differs: {row}; actual {actual}");
            checks++;
        }
        foreach (var row in json.GetProperty("bonus_cases").EnumerateArray())
        {
            var actual = SacredCreatureLootBonus.Adjust(row.GetProperty("bonus").GetUInt32(), row.GetProperty("reference").GetUInt32(),
                row.GetProperty("experience_maximum").GetUInt16(), balance);
            if (actual != row.GetProperty("adjusted").GetUInt32()) throw new Exception($"Native creature loot bonus adjustment differs: {row}; actual {actual}");
            checks++;
        }
        foreach (var row in json.GetProperty("level_cases").EnumerateArray())
        {
            var actual = SacredLootLevelFactor.Calculate(row.GetProperty("source_level").GetUInt16(), row.GetProperty("hero_level").GetUInt16(),
                row.GetProperty("difficulty").GetUInt32(), balance);
            if (Math.Abs(actual - row.GetProperty("factor").GetDouble()) > 1e-12)
                throw new Exception($"Native loot level factor differs: {row}; actual {actual:R}");
            checks++;
        }
        foreach (var row in json.GetProperty("find_cases").EnumerateArray())
        {
            var actual = SacredLootFind.Calculate(row.GetProperty("equipment_bonus").GetSingle(), row.GetProperty("difficulty").GetUInt32(), row.GetProperty("unlocks").GetByte());
            if (actual != row.GetProperty("effective_find").GetUInt16()) throw new Exception($"Native loot find stat differs: {row}; actual {actual}");
            checks++;
        }
        foreach (var row in json.GetProperty("equipment_cases").EnumerateArray())
        {
            var bonuses = new List<SacredEquipmentBonus>();
            if (row.GetProperty("enabled").GetBoolean())
                foreach (var item in row.GetProperty("records").EnumerateArray())
                {
                    var parent = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(Convert.FromHexString(item.GetProperty("parent_hex").GetString()!));
                    Add(parent);
                    var index = 0;
                    foreach (var socket in item.GetProperty("socket_hex").EnumerateArray())
                    {
                        if (parent.SlotTypes[index] is >= 1 and <= 4)
                            Add(MemoryMarshal.Read<SacredEquipmentInstanceLayout>(Convert.FromHexString(socket.GetString()!)));
                        index++;
                    }
                }
            var attributes = row.GetProperty("attributes").EnumerateArray().Select(v => v.GetUInt16()).ToArray();
            var actual = SacredLootBonusSum.Calculate(bonuses, attributes);
            if (actual != row.GetProperty("total").GetDouble()) throw new Exception($"Native equipment find sum differs: {row}; actual {actual:R}");
            checks++;
            void Add(SacredEquipmentInstanceLayout values)
            {
                for (var k = 0; k < 8; k++) bonuses.Add(SacredEquipmentBonusDecoder.Decode(values.BonusGroups[k], values.BonusTypes[k], values.BonusValues[k]));
            }
        }
        foreach (var row in json.GetProperty("creature_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var result = SacredLootRarity.Creature(weights, row.GetProperty("source_level").GetUInt16(), row.GetProperty("hero_level").GetUInt16(),
                row.GetProperty("difficulty").GetUInt32(), row.GetProperty("bonus").GetUInt16(), balance, random);
            if (result.Selector != row.GetProperty("selector").GetUInt32() || result.Quality != row.GetProperty("quality").GetUInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native creature rarity differs: {row}; actual {result}, RNG {random.State}");
            checks += 3;
        }
        foreach (var row in json.GetProperty("container_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var result = SacredLootRarity.Container(weights, row.GetProperty("find").GetUInt16(), row.GetProperty("survival_seconds").GetUInt32(), balance, random);
            if (result.Quality != row.GetProperty("quality").GetUInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native container rarity differs: {row}; actual {result}, RNG {random.State}");
            checks += 2;
        }
        return checks;
    }
}
