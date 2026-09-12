using System.Text;
using Sacred.Particles;
using Sacred.Particles.Reader;
using Sacred.Particles.Reader.Generation;

string? executable = null, output = null;
var check = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--exe" when i + 1 < args.Length: executable = args[++i]; break;
        case "--output" when i + 1 < args.Length: output = args[++i]; break;
        case "--check": check = true; break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            return 2;
    }
}
if (executable == null || output == null)
{
    Console.Error.WriteLine("Usage: Sacred.Particles.Reader --exe <Sacred.exe> --output <generated source directory> [--check]");
    return 2;
}

try
{
    // Prepare every quality before touching output so a failed decode cannot leave mixed sources.
    var generated = new List<(string Path, string Source)>();
    generated.Add((Path.Combine(output, "EmbeddedModelEffects.g.cs"), ModelEffectSourceWriter.Write(executable)));
    foreach (var quality in Enum.GetValues<SacredParticleQuality>())
    {
        var catalogue = SacredParticleCatalogueReader.Load(executable, new() { Quality = quality, Log = Console.WriteLine });
        if (catalogue.DecodedCount == 0) throw new InvalidDataException("Refusing to generate an entirely undecoded catalogue.");
        generated.Add((Path.Combine(output, $"EmbeddedParticleCatalogue{quality}.g.cs"), ParticleCatalogueSourceWriter.Write(catalogue)));
    }
    if (check)
    {
        var mismatches = generated.Where(g => !File.Exists(g.Path) || File.ReadAllText(g.Path).Replace("\r\n", "\n", StringComparison.Ordinal) != g.Source).ToArray();
        foreach (var mismatch in mismatches) Console.Error.WriteLine($"Generated catalogue is missing or stale: {mismatch.Path}");
        if (mismatches.Length != 0) return 1;
        Console.WriteLine("[particles] All embedded catalogue sources match the executable.");
    }
    else
    {
        Directory.CreateDirectory(output);
        foreach (var file in generated)
        {
            File.WriteAllText(file.Path, file.Source, new UTF8Encoding(false));
            Console.WriteLine($"[particles] Generated {file.Path}");
        }
    }
    return 0;
}
catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException or BadImageFormatException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
