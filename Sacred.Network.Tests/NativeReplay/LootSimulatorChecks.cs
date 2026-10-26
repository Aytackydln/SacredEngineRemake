using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;

internal static class LootSimulatorChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", name)));
        using var magic = Read("native-equipment-magic.json");
        using var ordinary = Read("native-equipment-factory.json");
        using var loot = Read("native-loot-simulator.json");
        var image = new byte[magic.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var chunk in magic.RootElement.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var data = EquipmentGenerationFixture.Read(root, ordinary.RootElement) with
            { SingleMagicBonusItem = SacredEquipmentMagicDefinitions.Read(image) };
        var simulator = new WorldEquipmentDropSimulator(data);
        var checks = 0;
        foreach (var row in loot.RootElement.GetProperty("rolls").EnumerateArray())
        {
            var request = new SacredEquipmentDropRequest(row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(),
                row.GetProperty("seed").GetUInt32(), Category: SacredItemCategory.Weapon, Quality: null)
            {
                RarityContext = new(Enum.Parse<SacredLootRarityProfile>(row.GetProperty("profile").GetString()!),
                    row.GetProperty("equipment_bonus").GetSingle(), row.GetProperty("survival_seconds").GetUInt32(),
                    row.GetProperty("source_level").GetUInt16(), row.GetProperty("creature_bonus").GetUInt16())
            };
            var result = simulator.Roll(request);
            var selected = row.GetProperty("selected");
            var expected = selected.ValueKind == JsonValueKind.Null ? (uint?)null : selected.GetUInt32();
            if (result.SelectedType != expected || result.FinalRandomState != row.GetProperty("final_seed").GetUInt32() ||
                !result.Values.Span.SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)) ||
                expected.HasValue && result.RolledQuality != row.GetProperty("quality").GetUInt32() ||
                !expected.HasValue && result.UnavailableReason is null)
                throw new Exception($"Native source-rarity laboratory differs: {row}; actual {result}, values {Convert.ToHexString(result.Values.Span)}");
            var replay = simulator.Roll(request);
            if (replay.SelectedType != result.SelectedType || replay.FinalRandomState != result.FinalRandomState ||
                !replay.Values.Span.SequenceEqual(result.Values.Span) || replay.SourceRarity != result.SourceRarity)
                throw new Exception("Source context replay differs.");
            checks += 5;
        }
        return checks;
    }
}
