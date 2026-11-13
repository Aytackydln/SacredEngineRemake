using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;

internal static class SpecialHealthFactorChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-special-health-factor.json")));
        var source = fixture.RootElement;
        var image = new byte[source.GetProperty("image_size").GetInt32()];
        foreach (var chunk in source.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var definitions = SacredCreatureHealthFactorDefinitions.Read(image);
        var balance = CreatureBalanceBinReader.Read(Convert.FromHexString(source.GetProperty("balance_hex").GetString()!));
        var first = source.GetProperty("cases")[0].GetProperty("factors");
        Check(balance.BehaviorHealthFactor == first[0].GetSingle() && balance.StatusHealthFactor == first[1].GetSingle(),
            "original Balance factor fields");
        var checks = 1;
        foreach (var row in source.GetProperty("cases").EnumerateArray())
        {
            var mode = row.GetProperty("mode").GetInt32();
            var health = row.GetProperty("health").GetInt32();
            var factor = row.GetProperty("factors")[(mode & 1) != 0 ? 0 : 1].GetSingle();
            var actual = mode == 0 ? health : definitions.ApplyStartup(health, factor,
                row.GetProperty("type").GetUInt32(), row.GetProperty("difficulty").GetUInt32(), row.GetProperty("predicate").GetBoolean());
            Check(actual == row.GetProperty("result").GetInt32(), $"native branch result: {row}");
            checks++;
        }
        foreach (var row in source.GetProperty("class_queries").EnumerateArray())
        {
            Check(((Sacred.Core.Pak.Creature.SacredCreatureClass)row.GetProperty("class_byte").GetByte() == Sacred.Core.Pak.Creature.SacredCreatureClass.Dragon) == row.GetProperty("result").GetBoolean(),
                "actual original class-table predicate");
            checks++;
        }
        return checks;
        static void Check(bool value, string message) { if (!value) throw new Exception("Special health: " + message); }
    }
}
