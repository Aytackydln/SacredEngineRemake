using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

internal static class CreatureEquipmentResultChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-equipment-results.json")));
        using var profiles = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-base-results.json")));
        var json = profiles.RootElement;
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var definitions = SacredCreatureCombatDefinitions.Read(image);
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var expected = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var pending = row.GetProperty("pending").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            uint flags = 0x2468ACE;
            foreach (var gear in row.GetProperty("gear").EnumerateArray().OrderBy(value => value.GetProperty("slot").GetInt32()))
            {
                var records = gear.GetProperty("records_hex").EnumerateArray().Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
                for (var index = 0; index < records.Length; index++)
                {
                    if (index != 0 && records[0][12 + index - 1] is not (1 or 2 or 3 or 4)) continue;
                    flags |= SacredCreatureEquipmentContributions.Apply(values, pending, records[index], (SacredItemCategory)gear.GetProperty("category").GetByte(),
                        new(row.GetProperty("resource").GetUInt32(), row.GetProperty("unarmed").GetBoolean(), index != 0, 0, definitions.Profile(row.GetProperty("profile").GetByte())));
                }
            }
            if (!values.SequenceEqual(expected) || !pending.SequenceEqual(row.GetProperty("pending_after").EnumerateArray().Select(value => value.GetInt32())) ||
                flags != row.GetProperty("flags_after").GetUInt32())
            {
                var at = Enumerable.Range(0, values.Length).FirstOrDefault(i => values[i] != expected[i], -1);
                throw new Exception($"Native equipment results differ at {at}, pending {string.Join(',', pending)}, flags {flags:X8}: {row}; actual {Convert.ToHexString(values)}");
            }
            checks += values.Length + 7;
        }
        return checks;
    }
}
