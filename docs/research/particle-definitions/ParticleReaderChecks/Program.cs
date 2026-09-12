using Sacred.Particles;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: ParticleReaderChecks <native-reference.json> [Sacred.exe]");
    return 2;
}
ParticleReaderChecks.CatalogueChecks.Run(args[0], args.Length == 2 ? args[1] : null);
var catalogue = SacredParticleCatalogue.LoadEmbedded();
Console.WriteLine($"Embedded: {catalogue.Definitions.Count} FX types; {catalogue.DecodedCount} decoded; executable {catalogue.ExecutableSha256}.");
return 0;
