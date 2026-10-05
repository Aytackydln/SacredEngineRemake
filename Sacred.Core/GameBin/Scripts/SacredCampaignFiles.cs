namespace Sacred.Core.GameBin.Scripts;

/// <summary>One complete campaign or multiplayer script set under the game's bin directory.</summary>
public sealed class SacredCampaignFiles
{
    public const string DefaultDirectoryName = "NetScriptCamp";
    private static readonly string[] RequiredFiles =
        ["DefPos.bin", "StartCode.bin", "FunkCode.bin", "Vectoren.bin", "QuestCode.bin", "QuestPoolCode.bin"];

    private SacredCampaignFiles(string directoryPath) => DirectoryPath = directoryPath;

    public string DirectoryPath { get; }
    public string Name => Path.GetFileName(DirectoryPath);
    public string DefPosPath => Path.Combine(DirectoryPath, "DefPos.bin");
    public string StartCodePath => Path.Combine(DirectoryPath, "StartCode.bin");
    public string FunctionCodePath => Path.Combine(DirectoryPath, "FunkCode.bin");
    public string VectorsPath => Path.Combine(DirectoryPath, "Vectoren.bin");
    public string QuestCodePath => Path.Combine(DirectoryPath, "QuestCode.bin");
    public string QuestPoolCodePath => Path.Combine(DirectoryPath, "QuestPoolCode.bin");

    /// <param name="selection">A bin subdirectory name, or an explicit script directory path.</param>
    public static SacredCampaignFiles Resolve(string gameDirectory, string? selection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        selection ??= DefaultDirectoryName;
        ArgumentException.ThrowIfNullOrWhiteSpace(selection);
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Path.IsPathRooted(selection) ? selection : Path.Combine(gameDirectory, "bin", selection)));
        foreach (var file in RequiredFiles)
            if (!File.Exists(Path.Combine(path, file)))
                throw new FileNotFoundException($"Campaign '{path}' is missing {file}.", Path.Combine(path, file));
        return new SacredCampaignFiles(path);
    }

    /// <summary>Discovers complete sets without assuming character or multiplayer directory names.</summary>
    public static IReadOnlyList<SacredCampaignFiles> Discover(string gameDirectory) =>
        Directory.EnumerateDirectories(Path.Combine(gameDirectory, "bin"))
            .Where(path => RequiredFiles.All(file => File.Exists(Path.Combine(path, file))))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SacredCampaignFiles(Path.GetFullPath(path)))
            .ToArray();
}
