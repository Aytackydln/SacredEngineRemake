using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Runtime;

internal static class CreatureBaseResultChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-base-results.json")));
        var json = fixture.RootElement;
        var definitions = json.GetProperty("definition_variants").EnumerateArray()
            .Select(row => SacredCreatureCombatDefinitions.Read(Image(row))).Append(SacredCreatureCombatDefinitions.Read(Image(json))).ToArray();
        // The portable fixture retains only this verified global prefix; bound
        // its count as an independently valid Balance.bin global block.
        var balanceBytes = Convert.FromHexString(json.GetProperty("balance_prefix_hex").GetString()!);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(balanceBytes, (uint)balanceBytes.Length - 4);
        var balance = CreatureResultBalanceBinReader.Read(balanceBytes);
        var checks = 0;
        foreach (var row in json.GetProperty("cases").EnumerateArray())
            foreach (var definition in definitions)
            {
                var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
                var expected = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
                SacredCreatureBaseResults.Apply(values, row.GetProperty("bonuses").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
                    row.GetProperty("ranged").GetBoolean(), definition.Profile(row.GetProperty("profile").GetByte()), balance);
                if (!values.SequenceEqual(expected))
                {
                    var at = Enumerable.Range(0, values.Length).First(i => values[i] != expected[i]);
                    throw new Exception($"Native base results differ at {at}: {row}; actual {Convert.ToHexString(values)}");
                }
                checks += values.Length;
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
