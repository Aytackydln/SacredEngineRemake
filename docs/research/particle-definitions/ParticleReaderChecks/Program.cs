using Sacred.Particles;

if (args.Length == 2 && args[0] == "--texture-channels-only") { await ParticleReaderChecks.TextureChannelChecks.Run(args[1]); return 0; }

if (args.Length == 2 && args[0] == "--expanding-bursts-only") { ParticleReaderChecks.ExpandingBurstChecks.Run(args[1]); return 0; }

if (args.Length == 2 && args[0] == "--ring-surfaces-only") { ParticleReaderChecks.RingSurfaceChecks.Run(args[1]); return 0; }
if (args.Length == 3 && args[0] == "--catalogue-only") { ParticleReaderChecks.CatalogueChecks.Run(args[1], args[2]); return 0; }

if (args.Length == 2 && args[0] == "--war-cry-sign-only") { ParticleReaderChecks.WarCrySignChecks.Run(args[1]); return 0; }
if (args.Length == 2 && args[0] == "--pulse-flare-only") { ParticleReaderChecks.PulseFlareChecks.Run(args[1]); return 0; }
if (args.Length == 2 && args[0] == "--column-flare-only") { ParticleReaderChecks.ColumnFlareChecks.Run(args[1]); return 0; }
if (args.Length == 2 && args[0] == "--sprite-flare-only") { ParticleReaderChecks.SpriteFlareChecks.Run(args[1]); return 0; }
if (args.Length == 2 && args[0] == "--controlled-flare-only") { ParticleReaderChecks.ControlledFlareChecks.Run(args[1]); return 0; }
if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: ParticleReaderChecks <native-reference.json> [Sacred.exe]");
    return 2;
}
ParticleReaderChecks.CatalogueChecks.Run(args[0], args.Length == 2 ? args[1] : null);
ParticleReaderChecks.ElementStoneChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "element-stone-native-reference.json"));
ParticleReaderChecks.ForgeElementChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "forge-element-native-reference.json"));
ParticleReaderChecks.GlyphChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "glyph-native-reference.json"));
ParticleReaderChecks.PongBatterChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "pong-batter-native-reference.json"));
ParticleReaderChecks.AnducarChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "anducar-native-reference.json"));
ParticleReaderChecks.LayeredFlareChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "layered-flare-native-reference.json"));
ParticleReaderChecks.StrikeSymbolChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "strike-symbol-native-reference.json"));
ParticleReaderChecks.RingShieldChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "shield-native-reference.json"));
ParticleReaderChecks.StrikeSymbolChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "shielded-native-reference.json"));
ParticleReaderChecks.StrikeSymbolChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "superfluid-native-reference.json"));
ParticleReaderChecks.ConfusionChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "confusion-native-reference.json"));
ParticleReaderChecks.FireCircleHealingChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "fire-circle-healing-native-reference.json"));
ParticleReaderChecks.ControlledFlareChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "controlled-flare-native-reference.json"));
ParticleReaderChecks.SpriteFlareChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "sprite-flare-native-reference.json"));
ParticleReaderChecks.ColumnFlareChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "column-flare-native-reference.json"));
ParticleReaderChecks.PulseFlareChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "pulse-flare-native-reference.json"));
ParticleReaderChecks.WarCrySignChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "war-cry-sign-native-reference.json"));
ParticleReaderChecks.RingSurfaceChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "ring-surface-native-reference.json"));
ParticleReaderChecks.ExpandingBurstChecks.Run(Path.Combine(Path.GetDirectoryName(args[0])!, "expanding-burst-native-reference.json"));
var catalogue = SacredParticleCatalogue.LoadEmbedded();
Console.WriteLine($"Embedded: {catalogue.Definitions.Count} FX types; {catalogue.DecodedCount} decoded; executable {catalogue.ExecutableSha256}.");
return 0;
