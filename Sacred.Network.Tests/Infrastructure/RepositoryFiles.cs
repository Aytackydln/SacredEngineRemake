namespace Sacred.Network.Tests.Infrastructure;

internal static class RepositoryFiles
{
    private static readonly Lazy<string> RootPath = new(FindRoot);
    public static string Root => RootPath.Value;
    public static string Evidence(string relative) => Path.Combine(Root, "docs", "game-server", "evidence", relative);
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Sacred.Network", "Gameplay", "GoldGameEvent.cs")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Run packet tests from a SacredItemSimulator checkout; native evidence lives under docs/game-server/evidence.");
    }
}
