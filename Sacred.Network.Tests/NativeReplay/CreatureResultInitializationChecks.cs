using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;

internal static class CreatureResultInitializationChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-result-initialization.json")));
        var json = fixture.RootElement;
        var variants = json.GetProperty("definition_variants").EnumerateArray()
            .Select(row => SacredCreatureResultDefinitions.Read(Image(row)))
            .Append(SacredCreatureResultDefinitions.Read(Image(json))).ToArray();
        var checks = 0;
        foreach (var row in json.GetProperty("cases").EnumerateArray())
            foreach (var rules in variants)
            {
                var input = row.GetProperty("inputs");
                var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
                var expected = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
                var regeneration = SacredCreatureResultInitialization.Apply(values,
                    rules.UsesSlowDefaultSpeeds(input.GetProperty("resource").GetUInt32()), input.GetProperty("reference").GetUInt32(),
                    input.GetProperty("difficulty").GetUInt32(), input.GetProperty("boost").GetBoolean(),
                    input.GetProperty("template_endurance").ValueKind == JsonValueKind.Null ? null : input.GetProperty("template_endurance").GetByte());
                if (!values.SequenceEqual(expected) || (regeneration ?? 0x12345678) != row.GetProperty("regeneration").GetUInt32())
                    throw new Exception($"Native creature result initialization differs: {row}; actual {Convert.ToHexString(values)}, regeneration {regeneration}.");
                checks += values.Length + 1;
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
