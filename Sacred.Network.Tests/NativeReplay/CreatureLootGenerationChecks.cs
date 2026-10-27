using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;

internal static class CreatureLootGenerationChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-loot-generation.json")));
        var json = fixture.RootElement;
        var balance = CreatureBalanceBinReader.Read(Convert.FromHexString(json.GetProperty("balance_hex").GetString()!));
        var checks = 0;
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var levels = SacredCreatureLootGeneration.Levels(row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(), balance);
            var bounds = row.GetProperty("bounds");
            if (levels.Minimum != bounds.GetProperty("minimum").GetInt32() || levels.Maximum != bounds.GetProperty("maximum").GetInt32())
                throw new Exception($"Native creature loot generation bounds differ: {row}; actual {levels}");
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var choice = SacredCreatureLootGeneration.Choose(levels, row.GetProperty("selector").GetUInt32(),
                row.GetProperty("forced").GetBoolean(), row.GetProperty("spawn").GetByte(), random);
            var expected = row.GetProperty("result");
            if (choice.Kind.ToString() != expected.GetProperty("kind").GetString() || unchecked((uint)choice.Level) != expected.GetProperty("level").GetUInt32() ||
                choice.Quality != expected.GetProperty("quality").GetByte() || choice.Sockets != expected.GetProperty("sockets").GetByte() ||
                random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native creature loot generation differs: {row}; actual {choice}, RNG {random.State}");
            checks += 7;
        }
        return checks;
    }
}
