using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class EquipmentRewardChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-reward.json")));
        var json = fixture.RootElement;
        var rules = SacredEquipmentRewardDefinitions.Read(Image(json));
        var categories = ItemCategorySelectionChecks.ReadRules(root).RewardCategories;
        var checks = 0;
        foreach (var variant in json.GetProperty("definition_variants").EnumerateArray())
        {
            if (SacredEquipmentRewardDefinitions.Read(Image(variant)) != rules) throw new Exception("Installed reward definitions differ.");
            checks++;
        }
        var reward = new WorldEquipmentReward(rules, categories);
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var inputs = row.GetProperty("inputs");
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var events = row.GetProperty("events");
            var index = 0;
            var actual = reward.Run(row.GetProperty("level").GetUInt16(), row.GetProperty("scratch").GetUInt32(), new(row.GetProperty("chance").GetUInt32(),
                row.GetProperty("minimum").GetUInt32(), row.GetProperty("maximum").GetUInt32(), row.GetProperty("increment").GetInt32()),
                random, new(
                    resource => Effect("Money", [resource, 0, 0, 0]),
                    () => inputs.GetProperty("hero").GetBoolean() ? 8U : 0,
                    query => Effect("Selection", [(uint)query.Category, query.Level, 100, 15, query.HeroPreference, query.Flags, 1, query.MaximumQuality])
                        ? inputs.GetProperty("selection").GetBoolean() ? 1729U : 0 : null,
                    resource => Effect("Creation", [resource]) ? new WorldEquipmentRewardObject(
                        inputs.GetProperty("physical").GetBoolean() ? 500U : 0, inputs.GetProperty("physical").GetBoolean(),
                        inputs.GetProperty("item").GetBoolean(), inputs.GetProperty("values").GetBoolean() ? 1729U : null) : null,
                    (item, generation) => Effect("Normalization", [generation.Level, generation.Quality, item.ValuesResource!.Value, generation.Sockets]),
                    item => Effect("PrepareLaunch", [item.Reference]),
                    (_, launch) => Effect("Launch", [launch.X, launch.Y, launch.Z, launch.SpinX, launch.SpinY, launch.SpinZ])));
            if (actual.Pending.ToString() != row.GetProperty("pending").GetString() || index != events.GetArrayLength() ||
                random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native equipment reward differs: {row}; actual {actual}, events {index}, RNG {random.State}");
            checks += 3;

            bool Effect(string kind, double[] arguments)
            {
                if (index >= events.GetArrayLength()) throw new Exception($"Unexpected reward {kind}: {row}");
                var expected = events[index++];
                // Original normalization arguments preserve signed DWORD bits.
                var values = kind == "Normalization" ? arguments.Select(value => (double)unchecked((uint)(long)value)).ToArray() : arguments;
                if (kind != expected.GetProperty("kind").GetString() || random.State != expected.GetProperty("seed").GetUInt32() ||
                    !values.SequenceEqual(expected.GetProperty("arguments").EnumerateArray().Select(value => value.GetDouble())))
                    throw new Exception($"Native reward action differs: {row}; actual {kind}({string.Join(',', values)}), RNG {random.State}");
                checks += arguments.Length + 2;
                if (kind == inputs.GetProperty("unresolved").GetString()) return false;
                for (var draw = 0; draw < inputs.GetProperty("draws").GetInt32(); draw++) random.Next();
                return true;
            }
        }
        return checks;
    }

    private static byte[] Image(JsonElement json)
    {
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        return image;
    }
}
