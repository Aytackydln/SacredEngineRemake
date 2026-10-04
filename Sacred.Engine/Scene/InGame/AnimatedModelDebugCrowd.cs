using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Asset-driven independent poses for renderer measurements; never changes world files.</summary>
internal sealed class AnimatedModelDebugCrowd
{
    private readonly List<SceneModel> _models = [];
    private readonly HashSet<SceneModel> _owned = [];
    private readonly List<SceneModel> _worldModels = [];
    private GrnAnimatedMesh? _source;
    private float _time;
    public int Count { get; set; }

    public void Update(SceneState scene, float deltaSeconds)
    {
        if (scene.Models.Count == 0 || (Count == 0 && _models.Count == 0 && _owned.Count == 0)) return;
        var player = scene.Models[0];
        var source = player.Geometry.Animation;
        if (!ReferenceEquals(_source, source) || _models.Count != Count)
        {
            _models.Clear();
            _source = source;
            if (source is not null)
                for (var i = 0; i < Count; i++)
                {
                    var animation = new GrnAnimatedMesh(source.BindMesh, source.Pose.Data, source.Animation);
                    animation.RequestPose(i * .037f);
                    var model = new SceneModel($"Debug instance {i + 1}: {player.Name}", animation.Mesh,
                        player.Position, player.Rotation, player.Scale, player.TextureAliases,
                        groundPlaneZ: player.GroundPlaneZ, blockRadius: (uint)player.GroundShadowRadius);
                    model.SetAnimatedMesh(animation);
                    model.SetModelProjection(player.ModelProjection);
                    _models.Add(model);
                }
        }
        _time += deltaSeconds;
        for (var i = 0; i < _models.Count; i++)
        {
            var model = _models[i];
            var animation = model.Geometry.Animation!;
            if (!ReferenceEquals(animation.Animation, source!.Animation)) animation.SetAnimation(source.Animation);
            animation.RequestPose(_time + i * .037f);
            var offset = new Vector2((i % 8 - 3.5f) * .65f, (i / 8 + 1) * .65f);
            model.SetPose(player.Position + new Vector3(offset, 0), player.Rotation,
                player.DepthAnchor + offset, player.GroundPlaneZ);
        }
        _worldModels.Clear();
        for (var i = 1; i < scene.Models.Count; i++)
            if (!_owned.Contains(scene.Models[i])) _worldModels.Add(scene.Models[i]);
        _owned.Clear();
        foreach (var model in _models) { _worldModels.Add(model); _owned.Add(model); }
        scene.SetWorldModels(_worldModels);
    }
}
