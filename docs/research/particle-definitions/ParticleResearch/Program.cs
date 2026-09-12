using System.Security.Cryptography;
using System.Text.Json;
using ParticleResearch;

if (args.Length == 1 && args[0] == "--self-test")
{
    Verification.Run();
    return;
}
if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: ParticleResearch <game directory> <sample directory> <output directory> [relative script path]");
    Console.Error.WriteLine("       ParticleResearch --self-test");
    Environment.ExitCode = 2;
    return;
}

Verification.Run();
var game = Path.GetFullPath(args[0]);
var samples = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var selectedScript = args.Length == 4 ? args[3] : "bin/TYPE_NPC_VAMPIRELADY/FunkCode.bin";
Directory.CreateDirectory(output);
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
var compactOptions = new JsonSerializerOptions(options) { WriteIndented = false };
void Save(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, options));

var exe = File.ReadAllBytes(Path.Combine(game, "Sacred.exe"));
var types = TypeCatalogue.ReadAndVerify(samples, exe);
Console.WriteLine($"Verified {types.Count:N0} type-name slots against Sacred.exe.");

// Inventory includes every file, but only known script containers are parsed as bytecode.
// This makes the scan's coverage explicit without interpreting unrelated archives as scripts.
Save("game-file-inventory.json", Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories)
    .Order(StringComparer.OrdinalIgnoreCase).Select(path => new
    {
        Path = Path.GetRelativePath(game, path).Replace('\\', '/'),
        ByteLength = new FileInfo(path).Length
    }).ToArray());
var scan = ScriptScanner.Scan(game, types);
Save("script-summary.json", new
{
    ExecutableSha256 = Convert.ToHexStringLower(SHA256.HashData(exe)),
    TypeCatalogueSource = "particle-emitter-samples/Sacred.exe.type-names.jsonl; raw slots verified, ID association inherited from sample catalogue",
    Scope = "All FunkCode.bin, StartCode.bin and sgf.bin files. Instructions are declarations, not evaluated live-world state.",
    Scripts = scan.Scripts,
    EffectTypes = scan.Effects.GroupBy(e => (e.Creation.TypeId, e.TypeName))
        .OrderBy(g => g.Key.TypeId).Select(g => new { g.Key.TypeId, g.Key.TypeName, CountInUniqueScripts = g.Count() }).ToArray()
});
using (var writer = File.CreateText(Path.Combine(output, "script-effects.jsonl")))
    foreach (var effect in scan.Effects) writer.WriteLine(JsonSerializer.Serialize(effect, compactOptions));

var source = scan.Scripts.Single(s => s.Path.Equals(selectedScript.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
var matches = SampleMatcher.Match(samples, scan.Effects.Where(e => e.ScriptSha256 == source.Sha256).ToArray());
Save("sample-matches.json", new
{
    Script = source,
    Method = "Nearest literal FX creation by fixture tile XY. All candidates within five tiles retained; nearest three otherwise. No pixel classification or item-ID rules.",
    Limitation = "Spatial evidence does not establish one-to-one fixture ownership or script execution. Fissure fixtures were provisional in the input observations.",
    Matches = matches
});
SampleMatcher.WriteMarkdown(Path.Combine(output, "sample-matches.md"), source.Path, matches);
Console.WriteLine($"Complete: {scan.Scripts.Count} script files, {scan.Scripts.Select(s => s.Sha256).Distinct().Count()} unique contents, {scan.Effects.Count:N0} FX instructions in unique contents.");
Console.WriteLine($"Samples: {matches.Count} total, {matches.Count(m => m.Candidates[0].TileDistance == 0)} with an exact tile match. Report: {output}");
