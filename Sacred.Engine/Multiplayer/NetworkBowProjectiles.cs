using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.GameplayHost.World;
using Sacred.World.Geometry;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>Graphics resources and animation-pose queries for gameplay-owned bow projectiles.</summary>
internal sealed class NetworkBowProjectiles(AssetManager assets)
{
    private readonly Dictionary<uint, (long Generation, SceneModel Model)> _models = [];
    private readonly Dictionary<uint, Task<WorldModelAsset?>> _assets = [];
    public GameplayBowProjectiles? Playback { get; private set; }
    public IEnumerable<SceneModel> Models => _models.Values.Select(entry => entry.Model);

    public void Update(SacredNetworkWorldBinding world, GameplayBowProjectiles? playback,
        IReadOnlyDictionary<uint, (string Key, NetworkActorPresentation Actor)> actors, float seconds)
    {
        Playback = playback;
        if (playback is null) { _models.Clear(); return; }
        playback.Update(seconds, request => actors.TryGetValue(request.Attack.ObjectReference, out var source) &&
                source.Actor.TryGetBowRelease(request.Attack, out var point, out var age,
                    playback.Entries.GetValueOrDefault(request.Reference)?.IsBfg == true ? "weapon_fx01" : "Bip01 L Hand") ? new(point, age) : null,
            reference => actors.TryGetValue(reference, out var target) && target.Actor.TryGetBowTarget(out var aim) ? aim : null,
            world.OutdoorHeightAt);
        foreach (var reference in _models.Keys.Where(id => !playback.Entries.TryGetValue(id, out var current) ||
                     current.Retired || current.Request.Generation != _models[id].Generation).ToArray()) _models.Remove(reference);
        foreach (var (reference, arrow) in playback.Entries)
        {
            if (arrow.Retired) continue;
            if (arrow.IsBfg) continue; // The world particle system draws its decoded billboard/ground layers.
            if (!_assets.TryGetValue(arrow.Type, out var load)) _assets[arrow.Type] = load = Load(arrow.Type);
            if (arrow.Flight is not { Active: true } flight) continue;
            if (!_models.TryGetValue(reference, out var entry))
            {
                if (!load.IsCompleted || load.GetAwaiter().GetResult() is not { Model.Mesh: { } mesh } asset) continue;
                var model = new SceneModel($"Network arrow {reference}, resource {arrow.Type}", mesh, Vector3.Zero, Vector3.Zero,
                    WorldModelPose.Scale, asset.TextureAliases, sourceOriginOffset: asset.Model.Diagnostics?.SourceOriginOffset ?? Vector3.Zero);
                model.SetModelProjection(DroppedItemModelPose.CameraProjection);
                _models[reference] = entry = (arrow.Request.Generation, model);
                EngineLog.WriteLine($"Network arrow model ready: ref {reference}; {asset.Item.ModelName}.");
            }
            entry.Model.SetModelOrientation(GameplayArrowOrientation.FromDirection(flight.Direction));
            var tile = GameplayProjectileCoordinates.ToTiles(flight.Position);
            entry.Model.SetPose(new(tile, GameplayProjectileCoordinates.ToScene(flight.Position).Z), Vector3.Zero, tile, world.OutdoorHeightAt(tile));
        }
    }
    private async Task<WorldModelAsset?> Load(uint type)
    {
        try { return await assets.LoadProjectileModelAsync(type).ConfigureAwait(false); }
        catch (Exception error) { EngineLog.WriteLine($"Network projectile resource {type} failed: {error.Message}."); return null; }
    }
}
