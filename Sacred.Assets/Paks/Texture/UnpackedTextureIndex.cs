namespace Sacred.Assets.Paks.Texture;

/// <summary>Publishes immutable lookup snapshots of pak/**/*.tga files.</summary>
internal sealed class UnpackedTextureIndex : IDisposable
{
    private readonly string _directory;
    private readonly object _sync = new();
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _reloadTimer;
    private Dictionary<string, UnpackedTextureFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public UnpackedTextureIndex(string directory)
    {
        _directory = Path.GetFullPath(directory);
        _reloadTimer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(_directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
        };
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Error += OnError;
        try
        {
            lock (_sync)
            {
                _watcher.EnableRaisingEvents = true;
                PublishFiles();
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool TryFind(string name, out UnpackedTextureFile file) =>
        Volatile.Read(ref _files).TryGetValue(name.Replace('\\', '/'), out file);

    private void OnChanged(object sender, FileSystemEventArgs args) => ScheduleReload();

    private void OnError(object sender, ErrorEventArgs args)
    {
        Console.WriteLine($"Unpacked texture watcher: {args.GetException().Message}");
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        lock (_sync)
        {
            if (!_disposed)
                _reloadTimer.Change(150, Timeout.Infinite);
        }
    }

    private void Reload()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            try
            {
                PublishFiles();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Unpacked texture index reload failed: {error.Message}");
                _reloadTimer.Change(500, Timeout.Infinite);
            }
        }
    }

    private void PublishFiles()
    {
        var files = Directory.EnumerateFiles(_directory, "*.tga", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var lookup = new Dictionary<string, UnpackedTextureFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files)
        {
            var name = Path.GetRelativePath(_directory, path).Replace('\\', '/');
            var file = new UnpackedTextureFile(name, path);
            var filename = Path.GetFileName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            lookup[filename] = file;
            lookup[stem] = file;
            if (stem.StartsWith("mix", StringComparison.OrdinalIgnoreCase))
                lookup[stem + ".444"] = file;
            if (stem.StartsWith("iso", StringComparison.OrdinalIgnoreCase) && int.TryParse(stem[3..], out var number))
            {
                for (var width = 1; width <= 4; width++)
                    lookup["iso" + number.ToString().PadLeft(width, '0') + ".tga"] = file;
            }
        }
        // Exact relative paths take priority over filename aliases.
        foreach (var path in files)
        {
            var name = Path.GetRelativePath(_directory, path).Replace('\\', '/');
            lookup[name] = new UnpackedTextureFile(name, path);
        }

        Volatile.Write(ref _files, lookup);
        Console.WriteLine($"Loaded unpacked texture index: {files.Length} TGA files in '{_directory}'.");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _watcher.Dispose();
            _reloadTimer.Dispose();
        }
    }
}

internal readonly record struct UnpackedTextureFile(string Name, string Path);
