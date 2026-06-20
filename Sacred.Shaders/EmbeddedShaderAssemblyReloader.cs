using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Sacred.Shaders;

/// <summary>Loads embedded shader resources from the most recently rebuilt shader assembly.</summary>
internal static class EmbeddedShaderAssemblyReloader
{
    private const int ReloadDelayMilliseconds = 250;
    private const string BuildOutputMetadataName = "SacredShadersBuildOutput";

    private static readonly object Sync = new();
    private static readonly Assembly OriginalAssembly = typeof(EmbeddedResources).Assembly;
    private static readonly string RebuildAssemblyPath = GetRebuildAssemblyPath();
    private static AssemblyLoadContext? _reloadedContext;
    private static Assembly? _reloadedAssembly;
    private static FileSystemWatcher? _watcher;
    private static Timer? _reloadTimer;
    private static Action? _rebuilt;
    private static byte[]? _loadedAssemblyHash;

    public static void WatchForRebuilds(Action rebuilt)
    {
        lock (Sync)
        {
            _rebuilt = rebuilt;
            if (_watcher is not null)
                return;

            if (string.IsNullOrWhiteSpace(RebuildAssemblyPath) || !File.Exists(RebuildAssemblyPath))
                return;

            _reloadTimer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(RebuildAssemblyPath)!, Path.GetFileName(RebuildAssemblyPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => ScheduleReload();
            _watcher.Created += (_, _) => ScheduleReload();
            _watcher.Renamed += (_, _) => ScheduleReload();
        }
    }

    public static byte[] ReadAllBytes(string resourceName)
    {
        lock (Sync)
        {
            var assembly = _reloadedAssembly ?? OriginalAssembly;
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded shader resource '{resourceName}' was not found.");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
    }

    private static void ScheduleReload()
    {
        lock (Sync)
            _reloadTimer?.Change(ReloadDelayMilliseconds, Timeout.Infinite);
    }

    private static void Reload()
    {
        try
        {
            Console.WriteLine("Assembly reload detected");
            var assemblyBytes = ReadAssemblyBytes(RebuildAssemblyPath);
            var hash = SHA256.HashData(assemblyBytes);

            AssemblyLoadContext? previousContext;
            lock (Sync)
            {
                if (_loadedAssemblyHash is not null && CryptographicOperations.FixedTimeEquals(_loadedAssemblyHash, hash))
                    return;

                var context = new AssemblyLoadContext($"Sacred.Shaders.HotReload.{Guid.NewGuid():N}", isCollectible: true);
                using var stream = new MemoryStream(assemblyBytes, writable: false);
                var assembly = context.LoadFromStream(stream);

                previousContext = _reloadedContext;
                _reloadedContext = context;
                _reloadedAssembly = assembly;
                _loadedAssemblyHash = hash;
            }

            previousContext?.Unload();
            _rebuilt?.Invoke();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Unable to reload embedded shaders: {exception}");
        }
    }

    private static byte[] ReadAssemblyBytes(string assemblyPath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = new MemoryStream();
                stream.CopyTo(output);
                return output.ToArray();
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static string GetRebuildAssemblyPath()
    {
        var buildOutputDirectory = OriginalAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == BuildOutputMetadataName)
            ?.Value;

        return string.IsNullOrWhiteSpace(buildOutputDirectory)
            ? OriginalAssembly.Location
            : Path.Combine(buildOutputDirectory, Path.GetFileName(OriginalAssembly.Location));
    }
}
