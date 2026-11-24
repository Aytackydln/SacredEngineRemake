using Lua;

namespace Sacred.GameServer.Scripting;

/// <summary>
/// Hot reload of mods and their <c>"unload"</c> lifecycle hook.
/// <code>
/// server.on("unload", function(reason) ... end)   -- reason: "reload" (this mod or all mods are about to be loaded again) or "shutdown"
/// </code>
/// An unload handler runs while the old script is still intact (same instruction budget as any handler) and is the place to save
/// state: <c>server.save(...)</c>, and <c>server.flush()</c> to put it on the disk. Afterwards everything the script registered
/// (handlers, timers, endpoints and pages, net handlers) is dropped. The mod's storage survives a reload (it is held by the host), so
/// the fresh script reads back what the old one saved, the same way a restart would. Writes in flight are drained before the hooks run (their callbacks are answered); callbacks of <c>server.flush</c> and
/// <c>server.save_hero</c> started by an unload hook of the old script are not run after a reload (the write still finishes before the mod is replaced); at shutdown they are.
/// <para>A changed, added or removed <c>*.lua</c> file reloads only that mod (the watcher, half a second after the last change);
/// <see cref="Reload"/> with a mod name does the same on demand, without a name every mod is reloaded in a fresh Lua state (a
/// full reload also resets global variables). A per-mod reload runs the file again in the shared Lua state: globals the old version
/// set stay until the new one overwrites them, and mods that talk to each other through globals should be reloaded together.
/// After a reload a "start" handler of the reloaded mod runs again (the other mods do not see another "start").</para>
/// </summary>
public sealed partial class ScriptHost
{
    private readonly object _changeLock = new();
    private HashSet<string>? _changedFiles;
    private bool _reloadAll;

    private void Touched(string? file)
    {
        if (file is not null && !file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) return;
        lock (_changeLock)
        {
            if (file is null) _reloadAll = true;
            else (_changedFiles ??= new(StringComparer.OrdinalIgnoreCase)).Add(Path.GetFileName(file));
        }
        _changed = true;
    }

    /// <summary>Game loop: carries out what the watcher and <see cref="RequestReload()"/> collected.</summary>
    private void ApplyPendingReload()
    {
        bool all; string[] files;
        lock (_changeLock)
        {
            all = _reloadAll; files = _changedFiles?.ToArray() ?? [];
            _reloadAll = false; _changedFiles = null;
        }
        if (all) { Log.Info("SCRIPT: reloading."); Load(); return; }
        foreach (var file in files) { Log.Info($"SCRIPT: reloading {file}."); ReloadFile(ModName(file)); }
    }

    /// <summary>Requests the reload of one mod (script file name without .lua) at the next pump, like the watcher does for a changed file.</summary>
    public void RequestReload(string mod) => Touched(mod + ".lua");

    private static string ModName(string file) => ScriptEndpoints.ModName(file);

    private string? FindFile(string mod)
    {
        if (!Directory.Exists(_options.Directory)) return null;
        foreach (var path in Directory.GetFiles(_options.Directory, "*.lua"))
            if (ModName(path) == mod) return path;
        return null;
    }

    /// <summary>
    /// Reloads one mod (<paramref name="mod"/> = script file name without .lua; the file may be new or gone) or, with null, every mod in a
    /// fresh Lua state. Game loop only. False when there is no such file and no such loaded mod.
    /// </summary>
    public bool Reload(string? mod)
    {
        if (mod is null) { Load(); return true; }
        mod = ModName(mod);
        if (FindFile(mod) is null && !_files.Any(f => ModName(f.File) == mod)) return false;
        ReloadFile(mod);
        return true;
    }

    private void ReloadFile(string mod)
    {
        if (_state is null) return;
        var path = FindFile(mod);
        var index = _files.FindIndex(f => ModName(f.File) == mod);
        if (index >= 0) UnloadFile(_files[index].File, "reload");
        if (path is null)
        {
            if (index >= 0) _files.RemoveAt(index);
            _descriptions.Remove(mod);
            Publish();
            Log.Info($"SCRIPT: {mod}.lua is gone: unloaded.");
            return;
        }
        var name = Path.GetFileName(path);
        var error = RunFile(path);
        if (index >= 0) _files[index] = (name, error);
        else
        {
            var at = _files.FindIndex(f => string.Compare(f.File, name, StringComparison.OrdinalIgnoreCase) > 0);
            _files.Insert(at < 0 ? _files.Count : at, (name, error));
        }
        Publish();
        Log.Info(error is null ? $"SCRIPT: loaded {name}." : $"SCRIPT: {name} failed to load: {error}");
        FireFile("start", name, default);
    }

    /// <summary>Runs "unload" handlers of every script, the last loaded first.</summary>
    private void FireUnloadAll(string reason)
    {
        WaitDurable();                                                      // writes in flight finish and answer their callbacks first
        foreach (var file in _files.Select(f => f.File).Reverse().ToArray()) FireFile("unload", file, One(reason));
    }

    /// <summary>The unload hook of one script and then everything it registered.</summary>
    private void UnloadFile(string file, string reason)
    {
        WaitDurable();
        FireFile("unload", file, One(reason));
        foreach (var write in _durable.ToArray()) if (write.File == file) { try { write.Task.Wait(); } catch (AggregateException) { } }   // what the hook flushed reaches the disk
        foreach (var list in _handlers.Values) list.RemoveAll(h => h.File == file);
        _timers.RemoveAll(t => t.File == file);
        _newTimers.RemoveAll(t => t.File == file);
        foreach (var key in _endpoints.Where(p => p.Value.File == file).Select(p => p.Key).ToArray()) _endpoints.Remove(key);
        foreach (var channel in _netChannels.Values) channel.Handlers.RemoveAll(h => h.File == file);
        foreach (var write in _durable) if (write.File == file) write.Function = null;
        foreach (var save in _heroSaves) if (save.File == file) save.Function = null;
        _descriptions.Remove(ModName(file));
        DropDeclarations(file);
    }

    /// <summary>Calls the handlers of <paramref name="name"/> that <paramref name="file"/> registered.</summary>
    private void FireFile(string name, string file, ReadOnlySpan<LuaValue> args)
    {
        if (!_handlers.TryGetValue(name, out var list)) return;
        for (var i = 0; i < list.Count; i++)
        {
            var handler = list[i];
            if (handler.File == file) Call(handler.Function, handler.File, args, ref handler.Errors, out _);
        }
    }

    private void Publish()
    {
        Volatile.Write(ref _endpointList, _endpoints.Values.ToArray());
        Volatile.Write(ref _mods, _files.Select(f => new ModEntry(ModName(f.File), _descriptions.GetValueOrDefault(ModName(f.File)), f.Error)).ToArray());
    }

    /// <summary>Runs one script file in the current state; the load error, or null.</summary>
    private string? RunFile(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            var source = File.ReadAllText(path);
            if (LeadingComment(source) is { } comment) _descriptions[ModName(name)] = comment;
            _currentFile = name;
            Run(() => _state!.DoStringAsync(source, name));
            return null;
        }
        catch (Exception e) when (Contained(e)) { return Describe(e); }
        finally { _currentFile = ""; }
    }

    /// <summary>
    /// The server is stopping: every mod's "unload" handler runs (reason "shutdown"), the durable writes they started are awaited and
    /// answered, then the scripts are off (no handler, timer or endpoint runs any more). Call <see cref="FlushStores"/> afterwards.
    /// </summary>
    public void Shutdown()
    {
        if (_state is null) return;
        FireUnloadAll("shutdown");
        WaitDurable();
        _state = null;
    }
}
