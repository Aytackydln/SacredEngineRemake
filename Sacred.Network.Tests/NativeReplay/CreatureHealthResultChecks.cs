using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.GameplayHost.Scripting;
using Sacred.Core.Runtime;

internal static class CreatureHealthResultChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-health-results.json")));
        using var tuning = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-passive-skills.json")));
        var balance = CreatureBalanceBinReader.Read(Convert.FromHexString(tuning.RootElement.GetProperty("balance_prefix_hex").GetString()!));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("inputs");
            var context = new WorldCreatureHealthContext(input.GetProperty("owner").GetByte(), input.GetProperty("local_reference").GetUInt32(),
                input.GetProperty("difficulty").GetUInt32(), input.GetProperty("network").GetBoolean(), input.GetProperty("players").GetUInt32(),
                0, 0, input.GetProperty("component_flags").GetUInt32(), false, input.GetProperty("hero").GetBoolean(),
                (SacredX87Precision)input.GetProperty("precision").GetInt32());
            var values = Convert.FromHexString(row.GetProperty("values_hex").GetString()!);
            var health = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            WorldCreatureHealthResults.Apply(values, health, context, balance);
            if (!health.SequenceEqual(Convert.FromHexString(row.GetProperty("after_hex").GetString()!)) ||
                !values.SequenceEqual(Convert.FromHexString(row.GetProperty("values_hex").GetString()!)))
                throw new Exception($"Native health result differs: {row}; actual {Convert.ToHexString(health)}");
            checks += values.Length + health.Length;
            if (context.Owner != context.LocalReference) continue;
            foreach (var unsupported in new[] { context with { BehaviorFlags = 0x80000 }, context with { StatusFlags = 0x4000 }, context with { Mounted = true } })
            {
                var before = (byte[])health.Clone();
                try { WorldCreatureHealthResults.Apply(values, health, unsupported, balance); throw new Exception("Unsupported boss/mount health accepted."); }
                catch (NotSupportedException) { checks++; }
                if (!health.SequenceEqual(before)) throw new Exception("Unsupported health partially mutated state.");
            }
        }
        return checks;
    }
}
