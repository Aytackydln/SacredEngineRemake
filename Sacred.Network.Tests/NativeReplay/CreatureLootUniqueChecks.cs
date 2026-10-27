using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class CreatureLootUniqueChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-loot-unique.json")));
        using var familyFixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-loot-special-resources.json")));
        using var factory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        using var rewardFixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-death-rewards.json")));
        var image = new byte[rewardFixture.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var chunk in rewardFixture.RootElement.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var rules = SacredCreatureDeathRewardDefinitions.Read(image);
        var data = EquipmentGenerationFixture.Read(root, factory.RootElement) with { CreatureDeathRewardRules = rules };
        var shared = new WorldEquipmentSharedValues(data);
        var variants = new WorldEquipmentUniqueVariants(data, shared);
        var families = familyFixture.RootElement.GetProperty("families").EnumerateArray().ToDictionary(
            row => row.GetProperty("key").GetInt32(), row => row.GetProperty("resources").EnumerateArray().Select(value => value.GetUInt32()).ToArray());
        families.Add(int.MaxValue, []);
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var inputs = row.GetProperty("inputs");
            var family = families[row.GetProperty("key").GetInt32()];
            IReadOnlyList<uint>? eligible = null;
            if (inputs.GetProperty("modifier").GetBoolean())
            {
                eligible = SacredEquipmentUniqueVariants.Eligible(family, row.GetProperty("hero_level").GetUInt16(),
                    rules.UniqueExcludedMinimum, rules.UniqueExcludedMaximum, shared.Candidate);
                if (!eligible.SequenceEqual(row.GetProperty("eligible").EnumerateArray().Select(value => value.GetUInt32())))
                    throw new Exception($"Native selector-zero family differs: {row}; actual [{string.Join(',', eligible)}]; exclusion {rules.UniqueExcludedMinimum}..{rules.UniqueExcludedMaximum}");
                // The first family resource resolves through the production
                // descriptor/modifier/archive path to the same ordered family.
                var prepared = family.FirstOrDefault(resource => data.Modifiers.Find(resource)?.Definition.WeaponType == row.GetProperty("key").GetInt32());
                if (prepared != 0 && !eligible.SequenceEqual(variants.Candidates(prepared, row.GetProperty("hero_level").GetUInt16())!))
                    throw new Exception("Shared host selector-zero family composition differs.");
                checks += 2;
            }
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var events = row.GetProperty("events");
            var index = 0;
            var actual = WorldCreatureLootUniqueReplacement.Run(eligible,
                new(row.GetProperty("minimum").GetInt32(), row.GetProperty("maximum").GetInt32()), random, new(
                    () => Effect("Retirement", [500]), () => Effect("Destruction", [500]),
                    resource => Effect("Creation", [resource]) ? shared.Candidate(resource).Values.Level : null,
                    level => Effect("Upgrade", [unchecked((uint)level)]),
                    generation => Effect("Normalization", [unchecked((uint)generation.Level), generation.Quality, 1729, generation.Sockets])));
            var expectedResource = row.GetProperty("replacement");
            if (actual.Pending.ToString() != row.GetProperty("pending").GetString() ||
                actual.Replacement != (expectedResource.ValueKind == JsonValueKind.Null ? null : expectedResource.GetUInt32()) ||
                index != events.GetArrayLength() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native selector-zero replacement differs: {row}; actual {actual}, events {index}, RNG {random.State}");
            checks += 4;

            bool Effect(string kind, uint[] arguments)
            {
                if (index >= events.GetArrayLength()) throw new Exception($"Unexpected unique effect {kind}: {row}");
                var expected = events[index++];
                if (kind != expected.GetProperty("kind").GetString() || random.State != expected.GetProperty("seed").GetUInt32() ||
                    !arguments.SequenceEqual(expected.GetProperty("arguments").EnumerateArray().Select(value => value.GetUInt32())))
                    throw new Exception($"Native unique effect differs: {row}; actual {kind}({string.Join(',', arguments)}), RNG {random.State}");
                checks += arguments.Length + 2;
                if (kind == inputs.GetProperty("unresolved").GetString()) return false;
                for (var draw = 0; draw < inputs.GetProperty("draws").GetInt32(); draw++) random.Next();
                return true;
            }
        }
        return checks;
    }
}
