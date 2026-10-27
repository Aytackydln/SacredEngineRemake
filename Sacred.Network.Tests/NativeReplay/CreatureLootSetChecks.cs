using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Sets;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class CreatureLootSetChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-loot-sets.json")));
        using var factory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var json = fixture.RootElement;
        var rules = SacredCreatureLootSetDefinitions.Read(Image(json));
        var data = EquipmentGenerationFixture.Read(root, factory.RootElement) with
            { CreatureLootSetRules = rules, CategoryRules = ItemCategorySelectionChecks.ReadRules(root) };
        var shared = new WorldEquipmentSharedValues(data);
        var selectors = json.GetProperty("tables").EnumerateArray().Select(table => new WorldCreatureLootSetSelection(
            data with { Sets = SetsBinArchive.Parse(Convert.FromHexString(table.GetString()!)) }, shared)).ToArray();
        var checks = 0;
        foreach (var variant in json.GetProperty("definition_variants").EnumerateArray())
        {
            if (SacredCreatureLootSetDefinitions.Read(Image(variant)) != rules) throw new Exception("Installed set-selection exclusions differ.");
            checks++;
        }
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var inputs = row.GetProperty("inputs");
            var equipment = inputs.GetProperty("equipment").EnumerateArray().Select(item => new SacredCreatureLootSetEquipment(
                item.GetProperty("has_object").GetBoolean(), item.GetProperty("excluded").GetBoolean(),
                item.GetProperty("resource").ValueKind == JsonValueKind.Null ? null : item.GetProperty("resource").GetUInt32())).ToArray();
            var hero = inputs.GetProperty("hero").GetBoolean() ? new SacredCreatureLootSetContext(row.GetProperty("hero_resource").GetUInt32(),
                row.GetProperty("hero_level").GetUInt16(), equipment) : null;
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var actual = selectors[row.GetProperty("table").GetInt32()].Select(1729, row.GetProperty("selector").GetUInt32(),
                row.GetProperty("forced").GetBoolean(), hero, random);
            var expected = row.GetProperty("result");
            SacredCreatureLootSetResult? native = expected.ValueKind == JsonValueKind.Null ? null : new(
                expected.GetProperty("resource").GetUInt32(), expected.GetProperty("selector").GetUInt32(),
                expected.GetProperty("set_index").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("set_index").GetInt32(),
                expected.GetProperty("preferred").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("preferred").GetBoolean());
            if (actual != native || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native creature set selection differs: {row}; actual {actual}, RNG {random.State}");
            checks += 6;
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
