using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;

namespace AssetViewer.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
    private GrnAnimatedMesh? _animatedModel;
    private float _animationTime;
    private bool _animationPlaying = true;

    public void SetAnimationPlaying(bool playing)
    {
        _animationPlaying = playing;
        _lastEffectUpdateTimestamp = Stopwatch.GetTimestamp();
    }

    public void SetAnimationTime(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        WaitForGpu();
        _animationTime = seconds;
        UpdateAnimatedModel(0, force: true);
        if (_animatedModel is not null && _equipmentEffectScene is not null && _equipmentEffectMesh is not null)
        {
            _equipmentEffectScene.ResetNativeEffects();
            // A seek changes the emitter discontinuously. Settle new chains at
            // this pose rather than showing their constructor positions at the origin.
            for (var step = 0; step < 120; step++) _equipmentEffectScene.ApplyPose(_animatedModel, 1f / 60f);
            UpdateUploadBuffer(_equipmentEffectMesh.VertexBuffer,
                MemoryMarshal.AsBytes(_equipmentEffectScene.Mesh.Vertices.AsSpan()));
        }
        _lastEffectUpdateTimestamp = Stopwatch.GetTimestamp();
        Console.WriteLine($"[Assets] Animation time: {seconds:F3}s.");
    }

    private Mesh PrepareAnimatedModel(GrnAsset asset)
    {
        _animatedModel = null;
        _animationTime = 0;
        _lastEffectUpdateTimestamp = Stopwatch.GetTimestamp();
        if (_assetPreview && asset.DefaultAnimation is { } animation && asset.Mesh is { } mesh && asset.Skin is { } skin)
        {
            _animatedModel = new GrnAnimatedMesh(mesh, skin, animation);
            _animatedModel.RequestPose(0);
            return _animatedModel.Mesh;
        }
        return asset.Mesh!;
    }

    private void UpdateAnimatedModel(float elapsed, bool force = false)
    {
        if (_animatedModel is null || _mesh is null || (!force && !_animationPlaying)) return;
        if (_animationPlaying) _animationTime += elapsed;
        _animatedModel.RequestPose(_animationTime);
        _sourceMesh = _animatedModel.Mesh;
        UpdateUploadBuffer(_mesh.VertexBuffer, MemoryMarshal.AsBytes(_sourceMesh.Vertices.AsSpan()));
    }
}
