using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;

namespace Sacred.Engine.Rendering;

/// <summary>Publishes immutable scenery snapshots; object walks and pixel analysis run on a loader thread.</summary>
internal sealed class TerrainStaticPreparationWorker(AssetManager assets) : IDisposable
{
    private readonly TerrainStaticSpriteBuilder _builder = new(assets);
    private readonly PrioritizedAssetLoadScheduler _scheduler = new("Sacred static scenery preparation");
    private Task<Result>? _task;
    private Sector[] _sectors = [];
    private IndoorTileGroup? _indoor;
    private long _generation;
    private long _nextRetry;
    private bool _dirty = true;
    private bool _assetsPending = true;
    private TerrainStaticPreparation _ready = new([], [], new HashSet<uint>(), false, false, 0, 0);
    // Accessed only by the worker, including when an obsolete result is discarded.
    private Sector[]? _builtSectors;

    public bool HasPendingAssetRequests => _dirty || _assetsPending || _task is not null;

    public TerrainStaticPreparation Prepare(IReadOnlyList<Sector> sectors, IndoorTileGroup? indoor)
    {
        if (!SameSectors(_sectors, sectors) || _indoor?.Id != indoor?.Id)
        {
            _sectors = new Sector[sectors.Count];
            for (var index = 0; index < sectors.Count; index++) _sectors[index] = sectors[index];
            _indoor = indoor;
            _generation++;
            _dirty = true;
        }

        var changed = false;
        if (_task is { IsCompleted: true })
        {
            var result = _task.GetAwaiter().GetResult();
            _task = null;
            if (result.Generation == _generation)
            {
                var promotionsChanged = !_ready.PromotedEmbeddedObjectIds.SetEquals(result.Preparation.PromotedEmbeddedObjectIds);
                _ready = result.Preparation with { PromotionsChanged = promotionsChanged };
                _assetsPending = result.AssetsPending;
                _dirty = false;
                changed = true;
            }
        }

        return _ready with { Changed = changed, PromotionsChanged = changed && _ready.PromotionsChanged };
    }

    public void OnForegroundFrameSubmitted()
    {
        if (_task is null && (_dirty || (_assetsPending && Stopwatch.GetTimestamp() >= _nextRetry)))
        {
            var requestedSectors = _sectors;
            var requestedIndoor = _indoor;
            var generation = _generation;
            _nextRetry = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 60;
            _task = _scheduler.Schedule(AssetLoadPriority.Visible, () =>
            {
                var worldChanged = !SameSectors(_builtSectors, requestedSectors);
                var preparation = _builder.Prepare(requestedSectors, worldChanged, true, requestedIndoor, true);
                _builtSectors = requestedSectors;
                // The builder reuses its collections. Never expose them to the frame thread.
                var snapshot = preparation with
                {
                    Sprites = Copy(preparation.Sprites),
                    Lights = Copy(preparation.Lights),
                    PromotedEmbeddedObjectIds = new HashSet<uint>(preparation.PromotedEmbeddedObjectIds)
                };
                return new Result(generation, snapshot, _builder.HasPendingAssetRequests);
            });
        }

    }

    private static T[] Copy<T>(IReadOnlyList<T> source)
    {
        var result = new T[source.Count];
        for (var index = 0; index < source.Count; index++) result[index] = source[index];
        return result;
    }

    private static bool SameSectors(IReadOnlyList<Sector>? left, IReadOnlyList<Sector> right)
    {
        if (left is null || left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
            if (!ReferenceEquals(left[index], right[index])) return false;
        return true;
    }

    public void Dispose() => _scheduler.Dispose();
    private sealed record Result(long Generation, TerrainStaticPreparation Preparation, bool AssetsPending);
}
