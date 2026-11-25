namespace Sacred.GameServer.Scripting;

/// <summary>
/// Durable writes for scripts that must not answer before their change is on disk (<c>server.flush(fn)</c>, ScriptHost.Durable.cs):
/// the store is serialized now on the game loop thread and written on a worker thread after any write still running, through a
/// temporary file that is flushed to the disk (<see cref="FileStream.Flush(bool)"/>) before it is moved over the old file. While a
/// durable write is out, and until the game loop has handled its outcome (<see cref="DurableDone"/>), the regular 10 s flush leaves
/// this store alone: if the write fails, the file keeps the state from before it, and the script can undo its change in memory
/// before anything else is written.
/// </summary>
public sealed partial class ModStore
{
    private int _durablePending;

    /// <summary>A durable write is running or its outcome has not been handled yet.</summary>
    public bool DurablePending => _durablePending > 0;

    /// <summary>Test hook: a write fails with this message instead of touching the disk (null: write).</summary>
    public Func<string?>? FailDurableWrites { get; set; }

    /// <summary>Starts a durable write of the whole store; the task gives null when the file is on disk, else the error.</summary>
    public Task<string?> FlushDurable()
    {
        var text = Serialize();
        _dirty = false;
        _durablePending++;
        var previous = _writing;
        var fail = FailDurableWrites;
        var task = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); } catch (Exception) { /* the earlier write logged its own failure */ }
            return fail?.Invoke() ?? WriteDurable(text);
        });
        _writing = task;
        return task;
    }

    /// <summary>The game loop handled a durable write's outcome. After a failure the store counts as changed, so the next flush
    /// writes what the script made of it (its undo, or the same change again).</summary>
    public void DurableDone(bool failed)
    {
        if (_durablePending > 0) _durablePending--;
        if (failed) _dirty = true;
    }

    private string? WriteDurable(string text)
    {
        var temporary = _path + ".tmp";
        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path));
            if (directory is not null) EnsureFolder(directory);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"SCRIPT: mod data '{_path}' not written (durable): {e.Message}");
            return e.Message;
        }
    }
}
