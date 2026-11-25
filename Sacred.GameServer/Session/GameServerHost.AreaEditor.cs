using System.Text;
using System.Text.Json;
using Sacred.GameServer.Data;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Session;

/// <summary>
/// The admin area editor's backend: the sector table and the world map of --game-dir (built once, on a worker thread, never on the
/// game loop) and the admin's access to a mod's storage (queued to the game loop like script endpoints, since a mod's storage belongs
/// to that thread).
/// </summary>
public sealed partial class GameServerHost
{
    private readonly object _areaMapLock = new();
    private Task<(byte[] Json, byte[] Png)>? _areaMapTask;

    public async Task<BinaryResponse> AreaMapRequest(bool image, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_config.GameDirectory) || !Directory.Exists(_config.GameDirectory))
            return new(404, "text/plain; charset=utf-8", "the area map needs --game-dir"u8.ToArray());
        Task<(byte[] Json, byte[] Png)> task;
        lock (_areaMapLock)
        {
            if (_areaMapTask is { IsFaulted: true }) _areaMapTask = null;                  // a failed read is tried again on the next request
            _areaMapTask ??= Task.Run(BuildAreaMap);
            task = _areaMapTask;
        }
        try
        {
            var (json, png) = await task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false);
            return image ? new(200, "image/png", png) : new(200, "application/json; charset=utf-8", json);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            Log.Info($"Area map: {e.Message}");
            return new(500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("the game files could not be read: " + e.Message));
        }
    }

    private (byte[] Json, byte[] Png) BuildAreaMap()
    {
        var map = AreaMap.Load(_config.GameDirectory!);
        var regions = RegionMapOrNull();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("available", true);
            writer.WriteNumber("width", map.Width);
            writer.WriteNumber("height", map.Height);
            writer.WriteNumber("tile", AreaMap.WorldUnitsPerTile);
            writer.WriteNumber("sectorTiles", AreaMap.TilesPerSector);
            writer.WriteStartObject("projection");
            writer.WriteNumber("ox", map.Projection.Ox); writer.WriteNumber("oy", map.Projection.Oy);
            writer.WriteNumber("kx", map.Projection.Kx); writer.WriteNumber("ky", map.Projection.Ky);
            writer.WriteEndObject();
            writer.WriteStartArray("regions");
            foreach (var region in regions?.Regions ?? [])
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", region.Id); writer.WriteString("key", region.Key); writer.WriteString("name", region.Name);
                writer.WriteNumber("sectors", regions!.SectorsOf.TryGetValue(region.Id, out var members) ? members.Length : 0);
                writer.WriteNumber("kills", region.KillsNeeded); writer.WriteBoolean("boss", region.Boss);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("sectors");
            foreach (var sector in map.Sectors)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", sector.Id); writer.WriteNumber("x", sector.CellX); writer.WriteNumber("y", sector.CellY);
                if (sector.Name.Length > 0) writer.WriteString("name", sector.Name);
                if (regions?.RegionAt(sector.Id) is > 0 and var region) writer.WriteNumber("r", region);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        Log.Info($"Area map built: {map.Sectors.Count} sectors, {map.Width}x{map.Height} map ({map.Png.Length / 1024} KB).");
        return (stream.ToArray(), map.Png);
    }

    // ---- the storage of a mod, for the admin ----------------------------------------------------------------------------------

    private sealed record StoreWork(string Mod, string? Key, string? Body, TaskCompletionSource<ModResponse> Done);
    private readonly System.Collections.Concurrent.ConcurrentQueue<StoreWork> _storeRequests = new();
    private int _storeQueued;

    public Task<ModResponse> ModStoreRequest(string mod, string? key, string? body, CancellationToken cancellationToken)
    {
        if (_scripts is null) return Task.FromResult(new ModResponse(404, "text/plain; charset=utf-8", "scripts are off"));
        if (Interlocked.Increment(ref _storeQueued) > 16)
        {
            Interlocked.Decrement(ref _storeQueued);
            return Task.FromResult(new ModResponse(503, "text/plain; charset=utf-8", "busy"));
        }
        var work = new StoreWork(mod, key, body, new TaskCompletionSource<ModResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
        _storeRequests.Enqueue(work);
        return work.Done.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    /// <summary>Game loop: the queued admin reads and writes of mod storage.</summary>
    private void PumpStoreRequests()
    {
        for (var i = 0; i < 4 && _storeRequests.TryDequeue(out var work); i++)
        {
            Interlocked.Decrement(ref _storeQueued);
            Scripting.ScriptResponse answer;
            try { answer = _scripts?.StoreRequest(work.Mod, work.Key, work.Body) ?? new(404, "text/plain; charset=utf-8", "scripts are off"); }
            catch (Exception e) when (e is not OutOfMemoryException) { Log.Info($"Mod storage request failed: {e.Message}"); answer = new(500, "text/plain; charset=utf-8", "failed"); }
            work.Done.TrySetResult(new ModResponse(answer.Status, answer.ContentType, answer.Body));
        }
    }
}
