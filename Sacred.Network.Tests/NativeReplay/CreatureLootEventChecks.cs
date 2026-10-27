using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Executable;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;

internal static class CreatureLootEventChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-loot-event.json")));
        var json = fixture.RootElement;
        var balance = LootBalanceBinReader.Read(Convert.FromHexString(json.GetProperty("balance_hex").GetString()!));
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var resources = SacredCreatureLootDefinitions.Read(image);
        var categories = ItemCategorySelectionChecks.ReadRules(root);
        var checks = 0;
        foreach (var row in json.GetProperty("plan_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var actual = SacredCreatureDeathLootPlan.Create(row.GetProperty("level").GetUInt16(), row.GetProperty("hero_level").GetUInt16(),
                row.GetProperty("difficulty").GetUInt32(), row.GetProperty("marked").GetBoolean(), row.GetProperty("boosted").GetBoolean(), balance, random);
            if (actual.Count != row.GetProperty("count").GetUInt32() || actual.Bonus != row.GetProperty("bonus").GetUInt32() ||
                actual.RequiresBoostedSourceDispatch != row.GetProperty("boosted").GetBoolean() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native death source count/bonus differs: {row}; actual {actual}, RNG {random.State}");
            checks += 4;
        }
        foreach (var row in json.GetProperty("potion_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var actual = SacredPotionLootSelection.Select(resources.PotionTable, row.GetProperty("difficulty").GetUInt32(),
                row.GetProperty("restricted").GetBoolean(), row.GetProperty("override").GetInt32(), random);
            if (actual != row.GetProperty("resource").GetUInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native potion selection/RNG differs: {row}; actual {actual}, RNG {random.State}");
            checks += 2;
        }
        foreach (var row in json.GetProperty("money_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var actual = SacredCreatureLootMoney.Calculate(row.GetProperty("level").GetUInt16(),
                balance with { CreatureMoneyMinimum = row.GetProperty("minimum").GetInt32(), CreatureMoneyMaximum = row.GetProperty("maximum").GetInt32() }, random);
            if (actual != row.GetProperty("amount").GetInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native money amount/RNG differs: {row}; actual {actual}, RNG {random.State}");
            checks += 2;
        }
        foreach (var row in json.GetProperty("death_cases").EnumerateArray())
        {
            var bonus = SacredCreatureDeathLootBonus.Calculate(row.GetProperty("base").GetUInt32(), row.GetProperty("reference").GetUInt32(),
                row.GetProperty("find").GetUInt16(), row.GetProperty("seconds").GetUInt32(), balance);
            if (bonus != row.GetProperty("bonus").GetUInt32()) throw new Exception($"Native death loot bonus differs: {row}; actual {bonus}");
            var count = row.GetProperty("count").GetUInt32();
            var calls = row.GetProperty("calls");
            if (calls.GetArrayLength() != count) throw new Exception("Native death caller count differs.");
            foreach (var call in calls.EnumerateArray())
            {
                var skill = row.GetProperty("skill");
                var survival = skill.ValueKind == JsonValueKind.Null ? 0f : skill.GetUInt16();
                if (call[0].GetUInt32() != 0 || call[1].GetUInt32() != bonus ||
                    call[2].GetUInt32() != BitConverter.SingleToUInt32Bits(survival) || call[3].GetUInt32() != row.GetProperty("reference").GetUInt32())
                    throw new Exception($"Native death loot arguments differ: {row}");
                checks += 4;
            }
            checks += 2;
        }
        foreach (var row in json.GetProperty("rune_cases").EnumerateArray())
        {
            var actual = SacredRuneLootChance.Calculate(row.GetProperty("source_level").GetUInt16(), row.GetProperty("level").GetUInt16(),
                row.GetProperty("klass").GetUInt32(), row.GetProperty("learned").GetUInt32(), row.GetProperty("runes").GetUInt32(),
                row.GetProperty("boosted").GetBoolean(), balance, 0);
            var expected = row.GetProperty("threshold").GetDouble();
            if (!double.IsFinite(actual) || Math.Abs(actual - expected) > Math.Max(1d, Math.Abs(expected)) * 1e-12)
                throw new Exception($"Native rune threshold differs: {row}; actual {actual:R}");
            checks++;
        }
        foreach (var row in json.GetProperty("attempt_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var index = 0;
            var rows = row.GetProperty("attempts");
            var complete = SacredCreatureLootAttempts.Run(row.GetProperty("chance").GetInt32(), row.GetProperty("forced").GetBoolean(), 100,
                17, row.GetProperty("experience").GetUInt16(), balance, random, attempt =>
                {
                    if (index >= rows.GetArrayLength()) throw new Exception($"Extra managed loot attempt: {row}");
                    var expected = rows[index++];
                    if (attempt.Attempt != expected.GetProperty("attempt").GetInt32() || attempt.RemainingBudget != expected.GetProperty("remaining").GetInt32() ||
                        attempt.Bonus != expected.GetProperty("bonus").GetUInt32() || random.State != expected.GetProperty("seed").GetUInt32())
                        throw new Exception($"Native loot attempt differs: {row}; actual {attempt}, RNG {random.State}");
                    for (var k = 0; k < row.GetProperty("body_draws").GetInt32(); k++) random.Next();
                    checks += 4;
                    return attempt.Attempt <= row.GetProperty("retries").GetInt32() ? SacredCreatureLootAttemptOutcome.RetrySelection : SacredCreatureLootAttemptOutcome.CompletedPass;
                });
            if (!complete || index != rows.GetArrayLength() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native loot attempt completion differs: {row}; RNG {random.State}");
            checks += 3;
        }
        foreach (var row in json.GetProperty("selection_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var state = new SacredCreatureLootSelectionState { LastRuneSource = row.GetProperty("last_rune").GetUInt32(), LastJewelrySource = row.GetProperty("last_jewelry").GetUInt32() };
            var chances = row.GetProperty("chances");
            var configuration = balance with { CreaturePotionChance = chances[0].GetInt32(), CreatureMoneyChance = chances[1].GetInt32(),
                CreatureWeaponChance = chances[2].GetInt32(), CreatureArmorChance = chances[3].GetInt32() };
            var actual = SacredCreatureLootSelection.Choose(new(17, row.GetProperty("source_resource").GetUInt32(), row.GetProperty("category").GetUInt32(),
                row.GetProperty("level").GetUInt16(), 2, row.GetProperty("selector").GetUInt32(), row.GetProperty("quality").GetByte()),
                state, resources, categories, configuration, random, () => row.GetProperty("threshold").GetDouble(), _ => row.GetProperty("rune_available").GetBoolean());
            var expected = row.GetProperty("choice");
            if (actual.Kind.ToString() != expected.GetProperty("kind").GetString() || actual.Selector != expected.GetProperty("selector").GetUInt32() ||
                random.State != row.GetProperty("final_seed").GetUInt32() || state.LastRuneSource != row.GetProperty("final_rune").GetUInt32() ||
                state.LastJewelrySource != row.GetProperty("final_jewelry").GetUInt32() || actual.EmitRuneEffect != row.GetProperty("rune_effect").GetBoolean())
                throw new Exception($"Native source choice/state/RNG differs: {row}; actual {actual}, RNG {random.State}");
            if (actual.Kind == SacredCreatureLootChoiceKind.Equipment)
            {
                var args = expected.GetProperty("arguments");
                if ((byte)actual.Category != args[0].GetByte() || row.GetProperty("level").GetUInt16() != (args[1].GetUInt32() & 65535) ||
                    args[2].GetUInt32() != 0 || args[3].GetUInt32() != 15 || actual.HeroPreference != args[4].GetUInt32() ||
                    actual.PreferenceFlags != args[5].GetByte() || args[6].GetUInt32() != 1 || actual.Quality != args[7].GetByte())
                    throw new Exception($"Native equipment source request differs: {row}; actual {actual}");
                checks += 8;
            }
            else if (actual.Kind == SacredCreatureLootChoiceKind.Resource && actual.Resource != expected.GetProperty("resource").GetUInt32())
                throw new Exception($"Native fixed resource differs: {row}; actual {actual}");
            checks += 6;
        }
        return checks;
    }
}
