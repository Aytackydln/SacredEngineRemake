using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.GameBin.Sets;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

internal static class CreatureDeathRewardChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-death-rewards.json")));
        var json = fixture.RootElement;
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var rules = SacredCreatureDeathRewardDefinitions.Read(image);
        var balance = LootBalanceBinReader.Read(Convert.FromHexString(json.GetProperty("balance_hex").GetString()!));
        var sets = SetsBinArchive.Parse(Convert.FromHexString(json.GetProperty("sets_hex").GetString()!));
        var checks = 0;
        foreach (var variant in json.GetProperty("definition_variants").EnumerateArray())
        {
            var variantImage = new byte[variant.GetProperty("image_size").GetInt32()];
            foreach (var chunk in variant.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(variantImage, chunk.GetProperty("offset").GetInt32());
            var actual = SacredCreatureDeathRewardDefinitions.Read(variantImage);
            if (actual.FirstHeroSet != rules.FirstHeroSet || actual.LastHeroSet != rules.LastHeroSet ||
                actual.UniqueExcludedMinimum != rules.UniqueExcludedMinimum || actual.UniqueExcludedMaximum != rules.UniqueExcludedMaximum ||
                !actual.HeroSetResources.SequenceEqual(rules.HeroSetResources) || !actual.JackpotCategories.SequenceEqual(rules.JackpotCategories) ||
                actual.Resources.Count != rules.Resources.Count || actual.Resources.Any(pair => !rules.Resources.TryGetValue(pair.Key, out var expected) || pair.Value != expected))
                throw new Exception($"Installed reward definitions differ: {variant.GetProperty("filename")}");
            checks += actual.Resources.Count + 7;
        }
        foreach (var row in json.GetProperty("ranges").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var value = SacredRandomRange.Inclusive(row.GetProperty("minimum").GetInt32(), row.GetProperty("maximum").GetInt32(), random);
            if (unchecked((uint)value) != row.GetProperty("value").GetUInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native inclusive range differs: {row}; actual {value}, RNG {random.State}");
            checks += 2;
        }
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var killer = new SacredCreatureDeathLootActor(1, row.GetProperty("killer_resource").GetUInt32(), 0, 0, 0, 100,
                row.GetProperty("killer_hero").GetBoolean() ? SacredCreatureClass.Hero : SacredCreatureClass.Monster);
            var effects = row.GetProperty("effects");
            var index = 0;
            var actual = SacredCreatureDeathRewardDispatcher.Run(new(7, 400, true), row.GetProperty("resource").GetUInt32(), 50,
                killer, sets, rules, balance, random, new(
                    equipment => Effect("Equipment", [equipment.Chance, equipment.MinimumQuality, equipment.MaximumQuality, unchecked((uint)equipment.LevelIncrement)]),
                    resource => Effect("ForcedDrop", [resource, 0, 0, 0]),
                    resource => resource == json.GetProperty("world_resource").GetUInt32() && row.GetProperty("exists").GetBoolean(),
                    query => Effect("UniqueQuery", [(uint)query.Category, query.SourceLevel, 100, 15, query.HeroPreference, 0, 1, 15])));
            if (actual.Plan.Count != row.GetProperty("count").GetUInt32() || actual.Plan.Bonus != row.GetProperty("bonus").GetUInt32() ||
                actual.Completed != row.GetProperty("completed").GetBoolean() || random.State != row.GetProperty("final_seed").GetUInt32() || index != effects.GetArrayLength())
                throw new Exception($"Native death reward dispatch differs: {row}; actual {actual}, RNG {random.State}, actions {index}");
            checks += 5;

            bool Effect(string kind, uint[] args)
            {
                if (index >= effects.GetArrayLength()) throw new Exception($"Unexpected death reward {kind}: {row}");
                var effect = effects[index++];
                if (effect.GetProperty("kind").GetString() != kind || effect.GetProperty("seed").GetUInt32() != random.State ||
                    !effect.GetProperty("arguments").EnumerateArray().Select(value => value.GetUInt32()).SequenceEqual(args))
                    throw new Exception($"Native death reward action differs: {row}; actual {kind}({string.Join(',', args)}), RNG {random.State}");
                checks += args.Length + 2;
                if (row.GetProperty("unresolved").GetBoolean()) return false;
                for (var draw = 0; draw < row.GetProperty("effect_draws").GetInt32(); draw++) random.Next();
                return true;
            }
        }
        return checks;
    }
}
