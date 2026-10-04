using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Scene;
using Sacred.Granny.Animation;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Shared immutable clip sources and independent DEFAULT pose outputs.</summary>
internal sealed class Dx12GpuPoseCache : IDisposable
{
    private readonly Dictionary<(GrnSkinningData, GrnAnimationClip), ModelGpuPoseSource> _sources = [];
    private readonly Dictionary<GrnPosePlayback, ModelGpuPoseInstance> _instances = new(ReferenceEqualityComparer.Instance);
    private readonly ID3D12Device _device;
    private readonly Dx12GeometryUploader _copies;
    private readonly int _frames;
    private Dx12PoseKernels? _kernels;
    public Dx12GpuPoseCache(ID3D12Device device, Dx12GeometryUploader copies, int frames)
    { _device = device; _copies = copies; _frames = frames; }
    public int InstanceCount => _instances.Count;
    public int SourceCount => _sources.Count;
    public bool IsReady => _kernels is not null;
    public long SourceBytes => _sources.Values.Sum(s => (long)s.Buffer.Description.Width);
    public long SourceCommittedBytes => _sources.Values.Sum(s => checked((long)_device.GetResourceAllocationInfo(0, s.Buffer.Description).SizeInBytes));
    public long Evaluations => _instances.Values.Sum(i => i.DispatchCount);
    public long OutputBytes => _instances.Values.Sum(i => Enumerable.Range(0, _frames).Sum(slot =>
        i.Buffer(slot) is { } buffer ? checked((long)buffer.Description.Width) : 0));
    public long OutputCommittedBytes => _instances.Values.Sum(i => Enumerable.Range(0, _frames).Sum(slot =>
        i.Buffer(slot) is { } buffer ? checked((long)_device.GetResourceAllocationInfo(0, buffer.Description).SizeInBytes) : 0));
    public void EnsurePipelines() => _kernels ??= new(_device);
    public void DisposePipelines()
    { _kernels?.Dispose(); _kernels = null; foreach (var instance in _instances.Values) instance.InvalidateOutputs(); }
    public void Prepare(IReadOnlyList<SceneModel> models, ID3D12GraphicsCommandList commands,
        Dx12FrameContext frame, Dx12GpuAnimationTimings timings)
    {
        var active = new HashSet<GrnPosePlayback>(ReferenceEqualityComparer.Instance);
        var sources = new HashSet<(GrnSkinningData, GrnAnimationClip)>();
        foreach (var model in models)
        {
            if (model.Geometry.Animation is not { } animation ||
                animation.Playback.Sink is not GpuPoseRequestSink || animation.Playback.LastRequest is not { } request) continue;
            if (request.Instance != animation.Playback.Instance) throw new InvalidOperationException("Expired GPU pose request.");
            active.Add(animation.Playback);
            var key = (animation.Pose.Data, request.Animation); sources.Add(key);
            if (!_sources.TryGetValue(key, out var source))
            {
                // Preparation-only copy/fence publication; never upload evaluated CPU palettes.
                var snapshot = new GpuPoseSourceData(key.Data, key.Animation);
                _sources.Add(key, source = new(_copies, snapshot));
                EngineLog.WriteLine($"GPU pose source prepared: {request.Animation.Name}; {snapshot.BoneCount} bones; {snapshot.Bytes.Length} bytes.");
            }
            if (!_instances.TryGetValue(animation.Playback, out var instance))
                _instances.Add(animation.Playback, instance = new(_frames));
            instance.Update(_device, commands, _kernels!, source, request, frame, timings);
        }
        foreach (var owner in _instances.Keys.Where(k => !active.Contains(k)).ToArray())
        { _instances[owner].Retire(frame); _instances.Remove(owner); }
        foreach (var key in _sources.Keys.Where(k => !sources.Contains(k)).ToArray())
        { _sources[key].Retire(frame); _sources.Remove(key); }
    }
    public bool TryGet(GrnPosePlayback playback, out ModelGpuPoseInstance instance) => _instances.TryGetValue(playback, out instance!);
    public void Dispose()
    { DisposePipelines(); foreach (var s in _sources.Values) s.Dispose(); foreach (var i in _instances.Values) i.Dispose(); _sources.Clear(); _instances.Clear(); }
}
