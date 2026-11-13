using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;

internal static class CreatureComponentResultChecks
{
    public static int Run(string root)
    {
        JsonDocument Fixture(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Fixture("native-creature-component-results");
        using var basis = Fixture("native-creature-base-results");
        using var initialization = Fixture("native-creature-result-initialization");
        using var finalization = Fixture("native-creature-result-finalization");
        using var precision = Fixture("native-startup-floating-point");
        using var passive = Fixture("native-creature-passive-skills");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { basis, initialization, finalization, precision })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var assets = WorldCreatureScalarResultAssets.Read(image, Convert.FromHexString(passive.RootElement.GetProperty("balance_prefix_hex").GetString()!));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var archive = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size * 2];
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
            Convert.FromHexString(row.GetProperty("template_hex").GetString()!).CopyTo(archive, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
            var template = CreaturePakArchive.Parse(archive).Entries[1];
            var context = new WorldCreatureScalarResultContext(template.ItemType, 0, 1, false, 0, 0, 0, row.GetProperty("fill").GetByte(),
                0, 0, false, default, 1, SacredX87Precision.Single);
            var events = row.GetProperty("events").EnumerateArray().ToArray();
            var delivered = 0;
            var actual = WorldCreatureComponentInitialization.Create(template, context, assets, (values, health) =>
            {
                var expected = events[delivered++];
                if (expected.GetProperty("kind").GetInt32() != 6 || !values.SequenceEqual(Bytes(expected, "values_hex")) || !health.SequenceEqual(Bytes(expected, "health_hex")))
                    throw new Exception($"Native component level event differs: {row}; values {Convert.ToHexString(values)}");
                checks += values.Length + health.Length + 1;
            });
            if (delivered != events.Length || !actual.Values.SequenceEqual(Bytes(row, "values_hex")) || !actual.Health.SequenceEqual(Bytes(row, "health_hex")) ||
                actual.CombatProfile != row.GetProperty("profile").GetByte() || actual.ResultValid != row.GetProperty("valid").GetByte())
                throw new Exception($"Native component construction differs: {row}; actual {Convert.ToHexString(actual.Values)}");
            checks += actual.Values.Length + actual.Health.Length + 3;
        }
        return checks;
    }

    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
