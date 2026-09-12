using ParticleEmitterDataset;

var game = args.ElementAtOrDefault(0) ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
var screenshots = args.ElementAtOrDefault(1) ?? @"C:\Users\Aytac\Pictures\Screenshots\Sacred\Particle Emitters";
var output = args.ElementAtOrDefault(2) ?? Path.GetFullPath("docs/research/particle-emitter-samples");
Directory.CreateDirectory(output);
if (args.Contains("--verify"))
{
    VerifyEvidence.Run(output);
    return;
}
if (args.Contains("--assets"))
{
    await new FixtureEvidence(game, output).Export();
    return;
}
var scenes = ImageEvidence.Export(screenshots, output);
await new ArchiveEvidence(game, output).Export(scenes);
Console.WriteLine("Particle evidence export complete.");
