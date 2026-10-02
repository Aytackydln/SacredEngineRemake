using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;

namespace Sacred.Engine.Rendering;

/// <summary>Resolves the predicted view's baked-sprite exclusions away from the render thread.</summary>
internal sealed class TerrainPrefetchPreparation(AssetManager assets) : IDisposable
{
    private readonly TerrainStaticSpriteBuilder _sprites = new(assets);
    private Task<PrefetchResult>? _task;
    private VisibleWorld? _requestedWorld;
    private VisibleWorld? _readyWorld;
    private IReadOnlySet<uint>? _promotions;

    public IReadOnlySet<uint>? Prepare(VisibleWorld world)
    {
        if (ReferenceEquals(_readyWorld, world)) return _promotions;
        if (_task is { IsCompleted: true })
        {
            if (_task.IsCompletedSuccessfully && ReferenceEquals(_requestedWorld, world) && _task.Result.Ready)
            {
                _readyWorld = world;
                _promotions = _task.Result.Promotions;
            }
            _task = null;
        }
        if (ReferenceEquals(_readyWorld, world)) return _promotions;
        if (_task is null && world.LoadingPreloadedSectors == 0 && world.PrefetchCenterSector is { } center && world.PreloadedSectors.Count > 0)
        {
            var sectors = new List<Sector>(9);
            foreach (var sector in world.Sectors)
                if (Math.Abs(sector.Coord.X - center.X) <= 1 && Math.Abs(sector.Coord.Y - center.Y) <= 1) sectors.Add(sector);
            sectors.AddRange(world.PreloadedSectors);
            _requestedWorld = world;
            _task = Task.Run(() =>
            {
                var result = _sprites.Prepare(sectors, true, true, null, true);
                return new PrefetchResult(!_sprites.HasPendingAssetRequests, new HashSet<uint>(result.PromotedEmbeddedObjectIds));
            });
        }
        return null;
    }

    public void Dispose() => _task?.GetAwaiter().GetResult();
    private sealed record PrefetchResult(bool Ready, IReadOnlySet<uint> Promotions);
}
