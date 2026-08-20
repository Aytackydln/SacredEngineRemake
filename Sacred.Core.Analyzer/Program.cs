using System.Text;

namespace Sacred.Core.Analyzer;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = AnalyzerOptions.Parse(args);
            var compilation = SacredCoreCompilation.Create(options.SourceDirectory);
            var analyzer = new LayoutAnalyzer(compilation);
            var cataloguedLayoutNames = GameFileCatalog.Files
                .SelectMany(static file => file.Sections)
                .Select(static section => section.LayoutTypeName)
                .Concat(analyzer.DiscoverLayoutTypeNames())
                .Distinct(StringComparer.Ordinal);
            var layouts = cataloguedLayoutNames
                .ToDictionary(static typeName => typeName, analyzer.Analyze, StringComparer.Ordinal);
            var discovered = GameFileScanner.Scan(options.GameDirectory);
            var report = new MarkdownReportWriter(GameFileCatalog.Files, layouts, discovered).Write();

            var outputPath = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"Analyzed {layouts.Count} layouts and {discovered.Count} installed game files.");
            Console.WriteLine($"Report written to {outputPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

internal sealed record AnalyzerOptions(string OutputPath, string SourceDirectory, string? GameDirectory)
{
    public static AnalyzerOptions Parse(IReadOnlyList<string> args)
    {
        var output = Path.Combine("docs", "game-file-formats.md");
        var sourceDirectory = "Sacred.Core";
        string? gameDirectory = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.Equals("--output", StringComparison.OrdinalIgnoreCase))
                output = ReadValue(args, ref index, argument);
            else if (argument.Equals("--source-directory", StringComparison.OrdinalIgnoreCase))
                sourceDirectory = ReadValue(args, ref index, argument);
            else if (argument.Equals("--game-directory", StringComparison.OrdinalIgnoreCase))
                gameDirectory = ReadValue(args, ref index, argument);
            else
                throw new ArgumentException($"Unknown argument '{argument}'. Use --source-directory <path>, --output <path>, and --game-directory <path>.");
        }

        return new AnalyzerOptions(output, sourceDirectory, gameDirectory);
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }
}
