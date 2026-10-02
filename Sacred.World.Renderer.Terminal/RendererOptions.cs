using System.Globalization;

namespace Sacred.World.Renderer.Terminal;

internal sealed record RendererOptions(
    string GameDirectory,
    string OutputDirectory,
    float? WorldX,
    float? WorldY,
    int Width,
    int Height,
    float Zoom)
{
    public bool OpenDoors { get; init; }
    public byte? IndoorLevel { get; init; }
    public bool AllSectors { get; init; }
    public int? SectorX { get; init; }
    public int? SectorY { get; init; }
    public ImageFormat Format { get; init; } = ImageFormat.Tga;
    public float ParticleSeconds { get; init; } = 2;
    public int TextureCacheMegabytes { get; init; } = 256;
    public bool SkipExisting { get; init; }
    public int ResolutionScale { get; init; } = 1;
    public int Supersampling { get; init; } = 2;
    public bool Verbose { get; init; }
    public RendererBackend Backend { get; init; } = RendererBackend.Software;
    private const string DefaultGameDirectory = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    public static RendererOptions Parse(string[] args)
    {
        string? gameDirectory = null;
        var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "world-debug-images");
        float? worldX = null;
        float? worldY = null;
        var width = 1280;
        var height = 720;
        var zoom = 0.75f;
        var openDoors = false;
        byte? indoorLevel = null;
        var allSectors = false;
        int? sectorX = null;
        int? sectorY = null;
        var format = ImageFormat.Tga;
        var particleSeconds = 2f;
        var textureCacheMegabytes = 256;
        var skipExisting = false;
        var viewportSpecified = false;
        var resolutionScale = 1;
        var supersampling = 2;
        var sectorQualitySpecified = false;
        var verbose = false;
        var backend = RendererBackend.Software;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) && gameDirectory is null)
            {
                gameDirectory = argument;
                continue;
            }

            switch (argument)
            {
                case "--game": gameDirectory = Read(args, ref index, argument); break;
                case "--output": outputDirectory = Read(args, ref index, argument); break;
                case "--world-x": worldX = ParseFloat(Read(args, ref index, argument), argument); break;
                case "--world-y": worldY = ParseFloat(Read(args, ref index, argument), argument); break;
                case "--width": width = ParsePositiveInt(Read(args, ref index, argument), argument); viewportSpecified = true; break;
                case "--height": height = ParsePositiveInt(Read(args, ref index, argument), argument); viewportSpecified = true; break;
                case "--zoom": zoom = ParsePositiveFloat(Read(args, ref index, argument), argument); viewportSpecified = true; break;
                case "--open-doors": openDoors = true; break;
                case "--indoor-level": indoorLevel = ParseByte(Read(args, ref index, argument), argument); break;
                case "--all-sectors": allSectors = true; break;
                case "--resolution": resolutionScale = ParseScale(Read(args, ref index, argument), argument); sectorQualitySpecified = true; break;
                case "--supersampling": supersampling = ParseScale(Read(args, ref index, argument), argument); sectorQualitySpecified = true; break;
                case "--sector-x": sectorX = ParseInt(Read(args, ref index, argument), argument); break;
                case "--sector-y": sectorY = ParseInt(Read(args, ref index, argument), argument); break;
                case "--format":
                    var value = Read(args, ref index, argument);
                    format = value.ToLowerInvariant() switch
                    {
                        "tga" => ImageFormat.Tga,
                        "bmp" => ImageFormat.Bmp,
                        _ => throw new ArgumentException("--format requires tga or bmp.")
                    };
                    break;
                case "--particle-seconds":
                    particleSeconds = ParseFloat(Read(args, ref index, argument), argument);
                    if (particleSeconds < 0 || particleSeconds > 60)
                        throw new ArgumentException("--particle-seconds requires a number from 0 through 60.");
                    break;
                case "--texture-cache-mb":
                    textureCacheMegabytes = ParseInt(Read(args, ref index, argument), argument);
                    if (textureCacheMegabytes < 0) throw new ArgumentException("--texture-cache-mb requires zero or a positive integer.");
                    break;
                case "--skip-existing": skipExisting = true; break;
                case "--verbose": verbose = true; break;
                case "--renderer":
                    backend = Read(args, ref index, argument).ToLowerInvariant() switch
                    {
                        "auto" => RendererBackend.Auto,
                        "gpu" => RendererBackend.Gpu,
                        "software" => RendererBackend.Software,
                        _ => throw new ArgumentException("--renderer requires auto, gpu or software.")
                    };
                    break;
                case "--help": throw new ShowHelpException();
                default: throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

        if (sectorX.HasValue != sectorY.HasValue)
            throw new ArgumentException("Pass both --sector-x and --sector-y.");
        if (allSectors && sectorX.HasValue)
            throw new ArgumentException("--all-sectors cannot be combined with a single sector.");
        if ((allSectors || sectorX.HasValue) && (worldX.HasValue || worldY.HasValue || viewportSpecified || indoorLevel.HasValue))
            throw new ArgumentException("Sector generation uses authored framing; coordinate, viewport, zoom and indoor options apply to the default view mode.");
        if (skipExisting && !allSectors)
            throw new ArgumentException("--skip-existing requires --all-sectors.");
        if (sectorQualitySpecified && !allSectors && !sectorX.HasValue)
            throw new ArgumentException("--resolution and --supersampling require sector generation mode.");
        if (format == ImageFormat.Tga && (width > ushort.MaxValue || height > ushort.MaxValue))
            throw new ArgumentException("TGA dimensions must be at most 65535 pixels.");
        if ((long)width * height * 4 > int.MaxValue)
            throw new ArgumentException("The viewport is too large for an RGBA image.");

        gameDirectory ??= Directory.Exists(DefaultGameDirectory) ? DefaultGameDirectory : null;
        if (gameDirectory is null)
            throw new ArgumentException("A Sacred installation directory is required. Pass it as the first argument or with --game.");
        return new RendererOptions(
            Path.GetFullPath(gameDirectory),
            Path.GetFullPath(outputDirectory),
            worldX,
            worldY,
            width,
            height,
            zoom)
        {
            OpenDoors = openDoors, IndoorLevel = indoorLevel, AllSectors = allSectors,
            SectorX = sectorX, SectorY = sectorY, Format = format, ParticleSeconds = particleSeconds,
            TextureCacheMegabytes = textureCacheMegabytes, SkipExisting = skipExisting,
            ResolutionScale = resolutionScale, Supersampling = supersampling, Verbose = verbose, Backend = backend
        };
    }

    public static string Help =>
        "Sacred.World.Renderer.Terminal <game-directory> [options]\n" +
        "  --output <directory>  Image destination (default: ./world-debug-images)\n" +
        "  --format <tga|bmp>    Output format (default: tga)\n" +
        "  --renderer <software|gpu|auto> Default: software; auto currently selects software\n" +
        "  --sector-x <integer>  Generate one MINIMAP texture (requires --sector-y)\n" +
        "  --sector-y <integer>  Sector grid Y, not a world tile coordinate\n" +
        "  --all-sectors         Generate every sector with an authored minimap texture\n" +
        "  --resolution <x1|x2|x4> Sector output dimensions relative to original (default: x1)\n" +
        "  --supersampling <x1|x2|x4> Render scale before area-filtered reduction (default: x2)\n" +
        "  --skip-existing       Resume an all-sector run, skipping existing outputs\n" +
        "  --verbose             Include loading, per-sector and rendering diagnostics\n" +
        "  --texture-cache-mb <n> Terminal RAM cache budget (default: 256 MiB; 0 disables retention)\n" +
        "  --particle-seconds <n> Simulated particle warm-up, 0 through 60 (default: 2)\n" +
        "  --world-x <number>    World X coordinate (default: start-sector center)\n" +
        "  --world-y <number>    World Y coordinate (default: start-sector center)\n" +
        "  --width <pixels>      In-game image width (default: 1280)\n" +
        "  --height <pixels>     In-game image height (default: 720)\n" +
        "  --zoom <number>       In-game camera zoom (default: 0.75)\n" +
        "  --open-doors          Sample authored activation clips at their final pose\n" +
        "  --indoor-level <n>    Select the authored indoor floor containing the world center";

    private static string Read(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count)
            throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }

    private static int ParsePositiveInt(string text, string option) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new ArgumentException($"{option} requires a positive integer.");

    private static int ParseInt(string text, string option) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : throw new ArgumentException($"{option} requires an integer.");

    private static int ParseScale(string text, string option) => text.ToLowerInvariant() switch
    {
        "x1" or "1" => 1,
        "x2" or "2" => 2,
        "x4" or "4" => 4,
        "x8" or "8" => 8,
        "x16" or "16" => 16,
        _ => throw new ArgumentException($"{option} requires x1, x2, x4, x8 or x16.")
    };

    private static float ParseFloat(string text, string option) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value
            : throw new ArgumentException($"{option} requires a finite number.");

    private static float ParsePositiveFloat(string text, string option)
    {
        var value = ParseFloat(text, option);
        return value > 0 ? value : throw new ArgumentException($"{option} requires a positive number.");
    }

    private static byte ParseByte(string text, string option) =>
        byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new ArgumentException($"{option} requires an integer from 1 through 255.");
}

internal sealed class ShowHelpException : Exception;

internal enum RendererBackend { Auto, Gpu, Software }
