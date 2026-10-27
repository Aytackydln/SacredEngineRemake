using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Sets;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class CreatureDeathPrefixChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", name)));
        using var fixture = Read("native-creature-death-prefix.json");
        using var factory = Read("native-equipment-factory.json");
        using var entry = Read("native-creature-loot-entry.json");
        using var reward = Read("native-creature-death-rewards.json");
        var image = new byte[entry.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { entry, reward })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var data = EquipmentGenerationFixture.Read(root, factory.RootElement) with
        {
            CreatureLootResources = SacredCreatureLootDefinitions.Read(image),
            CreatureDeathLootRules = SacredCreatureDeathLootDefinitions.Read(image),
            CreatureDeathRewardRules = SacredCreatureDeathRewardDefinitions.Read(image),
            Sets = SetsBinArchive.Parse(Convert.FromHexString(reward.RootElement.GetProperty("sets_hex").GetString()!))
        };
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("inputs");
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var source = new SacredCreatureDeathLootActor(10000, input.GetProperty("resource").GetUInt32(), 1,
                input.GetProperty("boosted").GetBoolean() ? 0x80000U : 0, 0, 100, (SacredCreatureClass)input.GetProperty("source_class").GetByte());
            var killer = new SacredCreatureDeathLootActor(input.GetProperty("killer_reference").GetUInt32(), input.GetProperty("killer_resource").GetUInt32(),
                0, 0, 0, 100, SacredCreatureClass.Hero);
            var hero = new SacredCreatureDeathLootActor(1, 2, 0, 0, 0, 100, SacredCreatureClass.Hero);
            var context = new WorldCreatureDeathLootContext(
                new(source, input.GetProperty("marked").GetBoolean() ? 0x400U : 0, 50, 100, false, input.GetProperty("destroyed").GetBoolean()),
                new(input.GetProperty("hero").GetBoolean() ? hero : null, new SacredCreatureLootObserver(0, 0, 100), 50,
                    input.GetProperty("find").GetUInt16(), input.GetProperty("seconds").GetUInt32(), input.GetProperty("skill").GetUInt16(),
                    input.GetProperty("rank").GetByte(), input.GetProperty("companion").GetBoolean() ? [killer.Reference] : []),
                input.GetProperty("killer_present").GetBoolean() ? killer : null, input.GetProperty("hostile").GetBoolean(), 0, 0);
            var configured = data with { Tuning = data.Tuning with { Loot = data.Tuning.Loot! with { DeathEquipmentDetachChance = input.GetProperty("detach_chance").GetInt32() } } };
            var prefix = new WorldCreatureDeathLootPrefix(configured);
            var expectedEvents = row.GetProperty("events");
            var eventIndex = 0;
            var actual = prefix.Run(context, random, new(
                () => Effect("EquipmentDetach", []),
                equipment => Effect("EquipmentReward", [equipment.Chance, equipment.MinimumQuality, equipment.MaximumQuality, (uint)equipment.LevelIncrement]),
                invocation => Effect("Invocation", [invocation.ForcedResource, invocation.Bonus, BitConverter.SingleToUInt32Bits(invocation.ChanceCurveInput), invocation.KillerReference]),
                _ => input.GetProperty("exists").GetBoolean(),
                query => Effect("UniqueReward", [(uint)query.Category, query.SourceLevel, 100, 15, query.HeroPreference, 0, 1, 15]),
                resource => SacredAuthoredLootSelection.Select(input.GetProperty("authored").GetBoolean()
                        ? [new(resource, 1729, 50)] : Array.Empty<SacredAuthoredLootRule>(), resource, random, () => input.GetProperty("time_seed").GetUInt32(), _ => true)));
            SacredCreatureDeathLootActor? expectedKiller = row.GetProperty("killer").GetString() switch { "hero" => hero, "killer" => killer, _ => null };
            var expectedPlan = row.GetProperty("plan");
            if (actual.Eligibility.Gate.ToString() != row.GetProperty("gate").GetString() || actual.Eligibility.Killer != expectedKiller ||
                actual.Eligibility.PhysicalFlags != row.GetProperty("flags").GetUInt32() || actual.Pending.ToString() != row.GetProperty("pending").GetString() ||
                random.State != row.GetProperty("final_seed").GetUInt32() || eventIndex != expectedEvents.GetArrayLength() ||
                actual.Plan.HasValue != (expectedPlan.ValueKind != JsonValueKind.Null))
                throw new Exception($"Native composed death prefix differs: {row}; actual {actual}, RNG {random.State}, events {eventIndex}");
            if (actual.Plan is { } plan && (plan.Count != expectedPlan.GetProperty("count").GetUInt32() || plan.Bonus != expectedPlan.GetProperty("bonus").GetUInt32()))
                throw new Exception($"Native composed source plan differs: {row}; actual {plan}");
            checks += 9;

            bool Effect(string kind, uint[] args)
            {
                if (eventIndex >= expectedEvents.GetArrayLength()) throw new Exception($"Extra death prefix effect {kind}: {row}");
                var expected = expectedEvents[eventIndex++];
                if (expected.GetProperty("kind").GetString() != kind || expected.GetProperty("seed").GetUInt32() != random.State ||
                    !args.SequenceEqual(expected.GetProperty("arguments").EnumerateArray().Select(v => v.GetUInt32())))
                    throw new Exception($"Native death prefix effect differs: {row}; actual {kind}({string.Join(',', args)}), RNG {random.State}");
                checks += args.Length + 2;
                if (input.GetProperty("unresolved").GetBoolean()) return false;
                for (var draw = 0; draw < input.GetProperty("draws").GetInt32(); draw++) random.Next();
                return true;
            }
        }
        return checks;
    }
}
