using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

/// <summary>Locks down the actual caller/RNG and the wider-mode reuse boundary.
/// Startup PC=24 outputs are retained independently; they must not be silently
/// replaced by the existing wider equipment generator.</summary>
internal static class StartupEquipmentGenerationChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Read("native-startup-equipment-generation");
        using var ordinary = Read("native-equipment-factory");
        using var names = Read("native-equipment-names");
        using var magic = Read("native-equipment-magic");
        var image = new byte[magic.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var chunk in magic.RootElement.GetProperty("definition_chunks").EnumerateArray())
            Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var data = EquipmentGenerationFixture.Read(root, ordinary.RootElement) with
        {
            Naming = EquipmentNameChecks.ReadNaming(names.RootElement),
            SingleMagicBonusItem = SacredEquipmentMagicDefinitions.Read(image)
        };
        var generator = new WorldEquipmentOrdinaryGeneration(data) { Naming = new WorldEquipmentNames(data) };
        var startupGenerator = new WorldEquipmentOrdinaryGeneration(data) { Naming = new WorldEquipmentNames(data), Precision = SacredX87Precision.Single };
        var checks = 0;
        var differences = 0;
        var startupFailures = new List<string>();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var legacy = (uint)(random.Next() % 4);
            var quality = (uint)(random.Next() % 4);
            var outputs = row.GetProperty("outputs");
            var startup = outputs[0];
            var wider = outputs[1];
            Check(startup.GetProperty("precision").GetInt32() == 24 && wider.GetProperty("precision").GetInt32() == 64, "original precision contexts");
            Check(quality == startup.GetProperty("quality").GetUInt32() && legacy == startup.GetProperty("legacy_selector").GetUInt32(), "caller draw order and quality");
            var type = row.GetProperty("type").GetUInt32();
            var values = generator.Generate(type, row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(),
                data.Templates.GetInstance(type), random, quality);
            Check(values.AsSpan().SequenceEqual(Bytes(wider, "result_hex")), "existing wider generator reuse");
            Check(random.State == wider.GetProperty("rng_after").GetUInt32() && generator.GeneratedName?.NativeReference == wider.GetProperty("name_reference").GetUInt32(),
                "actual object-context naming and caller RNG");
            if (!values.AsSpan().SequenceEqual(Bytes(startup, "result_hex")) || generator.GeneratedName?.NativeReference != startup.GetProperty("name_reference").GetUInt32()) differences++;
            var startupRandom = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            startupRandom.Next(); startupRandom.Next();
            var startupValues = startupGenerator.Generate(type, row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(),
                data.Templates.GetInstance(type), startupRandom, quality);
            var expected = Bytes(startup, "result_hex");
            if (!startupValues.AsSpan().SequenceEqual(expected) || startupRandom.State != startup.GetProperty("rng_after").GetUInt32() ||
                startupGenerator.GeneratedName?.NativeReference != startup.GetProperty("name_reference").GetUInt32())
                startupFailures.Add($"type {type}, level {row.GetProperty("level")}, seed {row.GetProperty("seed")}: bytes [{string.Join(',', Enumerable.Range(0, expected.Length).Where(at => expected[at] != startupValues[at]))}], RNG {startupRandom.State}/{startup.GetProperty("rng_after")}");
            checks += 2;
        }
        Check(differences == 28, "original PC=24 results prohibit using the wider generator for this startup path");
        if (startupFailures.Count != 0) throw new Exception($"{startupFailures.Count} startup named-generation differences:\n" + string.Join('\n', startupFailures.Take(12)));
        return checks;

        void Check(bool pass, string why) { if (!pass) throw new Exception("Startup equipment generation: " + why); checks++; }
    }

    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
