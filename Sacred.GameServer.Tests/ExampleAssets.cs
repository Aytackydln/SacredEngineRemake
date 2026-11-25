namespace Sacred.GameServer.Tests;

/// <summary>The example mods keep their pages in files next to the script (<c>scripts-examples/*.html|js|css</c>, read with
/// <c>server.file</c>); a test that installs a mod into a temporary scripts folder copies them along.</summary>
internal static class ExampleAssets
{
    private static readonly string[] Extensions = [".html", ".js", ".css"];

    public static void CopyTo(string scriptsFolder)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var examples = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples");
            if (!Directory.Exists(examples)) continue;
            Directory.CreateDirectory(scriptsFolder);
            foreach (var file in Directory.EnumerateFiles(examples))
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    File.Copy(file, Path.Combine(scriptsFolder, Path.GetFileName(file)), overwrite: true);
            return;
        }
        throw new DirectoryNotFoundException("scripts-examples");
    }
}
