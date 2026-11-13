using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;

internal static class StartupFloatingPointChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-startup-floating-point.json")));
        var json = fixture.RootElement;
        var checks = 0;
        foreach (var variant in json.GetProperty("definition_variants").EnumerateArray().Append(json))
        {
            var image = new byte[variant.GetProperty("image_size").GetInt32()];
            foreach (var chunk in variant.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
            var definition = SacredStartupFloatingPointDefinitions.Read(image);
            if (definition.Precision != SacredX87Precision.Single || definition.SiteCount != 3) throw new Exception("Original startup precision extraction differs.");
            checks += 2;
        }
        foreach (var row in json.GetProperty("control_cases").EnumerateArray())
        {
            var before = row.GetProperty("before").GetUInt16();
            var after = row.GetProperty("after").GetUInt16();
            // The CRT software conversion drops reserved bit 6. Compare the
            // defined exception/rounding/infinity fields, independently of PC.
            if ((after & 0x300) != 0 || ((before ^ after) & 0x1C3F) != 0) throw new Exception("Original startup control-word write differs.");
            checks += 2;
        }
        var differences = 0;
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("after_64_hex").GetString() != row.GetProperty("after_24_hex").GetString()) differences++;
            // An uncompared composition mode must fail before querying dependencies.
            var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var context = new WorldCreatureScalarResultContext(0, 100, 0, false, 0, 0, 0, 0, 0, 0, false, default, 1, SacredX87Precision.Double);
            try
            {
                WorldCreatureScalarResults.Calculate(values, null, context, [], null!, null!, null!, default, default, default);
                throw new Exception("Unverified startup precision accepted.");
            }
            catch (NotSupportedException) { checks++; }
            if (!values.SequenceEqual(Convert.FromHexString(row.GetProperty("before_hex").GetString()!))) throw new Exception("Precision guard mutated input.");
            checks++;
        }
        if (differences == 0 || differences != json.GetProperty("scalar_difference_cases").GetInt32()) throw new Exception("Original precision difference evidence lost.");
        checks++;
        using var tuning = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-passive-skills.json")));
        var balance = CreatureBalanceBinReader.Read(Convert.FromHexString(tuning.RootElement.GetProperty("balance_prefix_hex").GetString()!));
        var guardedHealth = new byte[24];
        try
        {
            WorldCreatureHealthResults.Apply(new byte[238], guardedHealth, new(1, 1, 0, true, 1, 0, 0, 0, false, false, (SacredX87Precision)0), balance);
            throw new Exception("Unverified startup health precision accepted.");
        }
        catch (NotSupportedException) { checks++; }
        if (guardedHealth.Any(value => value != 0)) throw new Exception("Precision guard partially mutated health.");
        return checks;
    }
}
