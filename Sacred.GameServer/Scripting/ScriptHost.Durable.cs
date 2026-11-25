using Lua;

namespace Sacred.GameServer.Scripting;

/// <summary>
/// <c>server.flush([fn])</c>: writes this mod's storage now, durably (<see cref="ModStore.FlushDurable"/>: temporary file flushed
/// to the disk, then moved over the old file), without blocking the game loop; <c>fn(ok, err)</c> runs on the game loop thread
/// afterwards (at the next pump once the write is done). For mods that must not answer a player before a change is on disk
/// (the auction house: a delivery answered and then lost in a crash before the 10 s flush could be collected twice). While a
/// durable write is out the regular flush skips this mod's file, so after a failure the file still holds the state from
/// before the write and the script can undo its change in memory first. Callbacks run in the order the writes were started.
/// A reload drops the callbacks of the old scripts (the writes still finish).
/// </summary>
public sealed partial class ScriptHost
{
    private sealed class DurableWrite
    {
        public required Task<string?> Task;
        public required ModStore Store;
        public required LuaFunction? Function;
        public required string File;
        public required LuaState State;
        public int Errors;
    }

    private readonly List<DurableWrite> _durable = new();

    /// <summary>Durable writes started and not yet answered (tests, diagnostics).</summary>
    public int DurableWritesPending => _durable.Count;

    private void AddDurable(LuaTable server)
    {
        server["flush"] = Fn("flush", c =>
        {
            var function = c.HasArgument(0) && c.GetArgument(0).Type == LuaValueType.Function ? c.GetArgument(0).Read<LuaFunction>() : null;
            var store = CurrentStore;
            _durable.Add(new DurableWrite { Task = store.FlushDurable(), Store = store, Function = function, File = _currentFile, State = _state! });
            return c.Return(true);
        });
        AddHeroes(server);                                                  // hero items and saves (ScriptHost.Heroes.cs)
    }

    /// <summary>Game loop: answers finished durable writes in the order they started.</summary>
    private void PumpDurable()
    {
        PumpHeroSaves();
        while (_durable.Count > 0 && _durable[0].Task.IsCompleted)
        {
            var write = _durable[0];
            _durable.RemoveAt(0);
            var error = write.Task.IsCompletedSuccessfully ? write.Task.Result : write.Task.Exception?.GetBaseException().Message ?? "write failed";
            write.Store.DurableDone(error is not null);
            if (write.Function is null || !ReferenceEquals(write.State, _state)) continue;
            _two[0] = error is null; _two[1] = error is null ? LuaValue.Nil : error;
            Call(write.Function, write.File, _two, ref write.Errors, out _);
        }
    }

    /// <summary>Waits until every durable write has finished and answers them (tests, shutdown).</summary>
    public void WaitDurable()
    {
        foreach (var write in _durable.ToArray()) { try { write.Task.Wait(); } catch (AggregateException) { } }
        PumpDurable();
    }
}
