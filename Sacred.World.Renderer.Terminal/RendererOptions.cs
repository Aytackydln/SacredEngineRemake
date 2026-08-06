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
                case "--width": width = ParsePositiveInt(Read(args, ref index, argument), argument); break;
                case "--height": height = ParsePositiveInt(Read(args, ref index, argument), argument); break;
                case "--zoom": zoom = ParsePositiveFloat(Read(args, ref index, argument), argument); break;
                case "--help": throw new ShowHelpException();
                default: throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

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
            zoom);
    }

    public static string Help =>
        "Sacred.World.Renderer.Terminal <game-directory> [options]\n" +
        "  --output <directory>  BMP destination (default: ./world-debug-images)\n" +
        "  --world-x <number>    World X coordinate (default: start-sector center)\n" +
        "  --world-y <number>    World Y coordinate (default: start-sector center)\n" +
        "  --width <pixels>      In-game image width (default: 1280)\n" +
        "  --height <pixels>     In-game image height (default: 720)\n" +
        "  --zoom <number>       In-game camera zoom (default: 0.75)";

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

    private static float ParseFloat(string text, string option) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value
            : throw new ArgumentException($"{option} requires a finite number.");

    private static float ParsePositiveFloat(string text, string option)
    {
        var value = ParseFloat(text, option);
        return value > 0 ? value : throw new ArgumentException($"{option} requires a positive number.");
    }
}

internal sealed class ShowHelpException : Exception;
