using System.Text.RegularExpressions;

namespace Sacred.Core.Analyzer;

internal static partial class GameFileScanner
{
    private static readonly HashSet<string> IncludedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pak", ".bin", ".keyx", ".wldx", ".res", ".tmp" };

    public static IReadOnlyList<DiscoveredGameFile> Scan(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return [];

        var files = new List<DiscoveredGameFile>();
        foreach (var rootName in new[] { "pak", "bin", "World", "scripts" })
        {
            var root = Path.Combine(gameDirectory, rootName);
            if (!Directory.Exists(root))
                continue;

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!IncludedExtensions.Contains(Path.GetExtension(path)))
                    continue;
                if (rootName.Equals("scripts", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(path).Equals("global.res", StringComparison.OrdinalIgnoreCase))
                    continue;

                files.Add(new DiscoveredGameFile(
                    Normalize(Path.GetRelativePath(gameDirectory, path)),
                    new FileInfo(path).Length));
            }
        }

        return files.OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool Matches(string pattern, string path)
    {
        var escaped = Regex.Escape(Normalize(pattern));
        escaped = escaped.Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return Regex.IsMatch(Normalize(path), $"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
