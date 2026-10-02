using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Scene;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Render-thread ownership and publication; copy work runs on the asset preparation scheduler.</summary>
internal sealed class Dx12SkinPreparationCache : IDisposable
{
    private readonly record struct Key(Mesh Mesh, GrnSkinningData Skin);
    private readonly Dictionary<Key, ModelGpuSkinSource> _sources = [];
    private readonly Dictionary<Key, Task<ModelGpuSkinSource>> _loads = [];
    private readonly HashSet<Key> _failed = [];
    private readonly Dictionary<GrnPose, ModelGpuSkinInstance> _instances = new(ReferenceEqualityComparer.Instance);
    private readonly Dx12GeometryUploader _copies;
    private readonly Dx12TextureUploader _uploader;
    private readonly Func<Func<ModelGpuSkinSource>, Task<ModelGpuSkinSource>> _schedule;
    private readonly int _frameCount;
    public Dx12SkinPreparationCache(ID3D12Device device, Dx12TextureUploader uploader, int frameCount,
        Func<Func<ModelGpuSkinSource>, Task<ModelGpuSkinSource>> schedule)
    { _copies = new(device, uploader); _uploader = uploader; _frameCount = frameCount; _schedule = schedule; }
    public int SourceCount => _sources.Count;
    public int InstanceCount => _instances.Count;
    public long PaletteBytes => _instances.Values.Sum(instance =>
        Enumerable.Range(0, _frameCount).Sum(slot => instance.Buffer(slot) is { } buffer ? checked((long)buffer.Description.Width) : 0L));
    public long SourceBytes => _sources.Values.Sum(s => s.ByteCount);
    public long UploadedPaletteBytes { get; private set; }
    public bool HasFailed(SceneModelGeometry geometry) => geometry.Animation is { } animation &&
        _failed.Contains(new(animation.BindMesh, animation.Pose.Data));

    // Called once after AcquireFrame's existing fence wait, before any surface/shadow draws.
    public void Prepare(IReadOnlyList<SceneModel> models, Dx12FrameContext retiredFrame)
    {
        var active = new HashSet<Key>();
        var poses = new Dictionary<GrnPose, Key>(ReferenceEqualityComparer.Instance);
        foreach (var model in models)
            if (model.Geometry.Animation is { } animation && animation.BindMesh.Vertices.Length > 0 && animation.BindMesh.Indices.Length > 0)
            { var key = new Key(animation.BindMesh, animation.Pose.Data); active.Add(key); poses.TryAdd(animation.Pose, key); }
        foreach (var key in _sources.Keys.Where(k => !active.Contains(k)).ToArray())
        { _sources[key].Retire(retiredFrame); _sources.Remove(key); }
        foreach (var pose in _instances.Keys.Where(p => !poses.ContainsKey(p)).ToArray())
        { _instances[pose].Retire(retiredFrame); _instances.Remove(pose); }
        _failed.RemoveWhere(k => !active.Contains(k));
        foreach (var key in _loads.Keys.Where(k => _loads[k].IsCompleted).ToArray())
        {
            var task = _loads[key]; _loads.Remove(key);
            if (task.IsCompletedSuccessfully)
            {
                if (active.Contains(key)) { _sources.Add(key, task.Result); EngineLog.WriteLine($"GPU skin source copy completed: {task.Result.ByteCount} bytes."); }
                else task.Result.Dispose(); // Never published or used by a graphics submission.
            }
            else { if (active.Contains(key)) _failed.Add(key); EngineLog.WriteLine($"GPU skin preparation failed: {task.Exception}"); }
        }
        foreach (var key in active)
            if (!_sources.ContainsKey(key) && !_loads.ContainsKey(key) && !_failed.Contains(key))
            { var snapshot = new GpuSkinSourceData(key.Mesh, key.Skin); _loads.Add(key, _schedule(() => new ModelGpuSkinSource(_copies, snapshot))); }
        foreach (var pair in poses)
        {
            var pose = pair.Key;
            if (!_instances.TryGetValue(pose, out var instance)) _instances.Add(pose, instance = new(pose, _frameCount));
            // Do not upload palettes for a source that is still copying.
            if (!_sources.ContainsKey(pair.Value)) continue;
            var before = instance.UploadedBytes; instance.Update(_uploader, retiredFrame);
            UploadedPaletteBytes += instance.UploadedBytes - before;
        }
    }
    public bool TryGet(SceneModelGeometry geometry, int slot, out ModelGpuSkinSource source, out ModelGpuSkinInstance instance)
    {
        source = null!; instance = null!;
        return geometry.Animation is { } animation && _sources.TryGetValue(new(animation.BindMesh, animation.Pose.Data), out source!) &&
            _instances.TryGetValue(animation.Pose, out instance!) && animation.Pose.IsEvaluated && instance.Revision(slot) == animation.Pose.Revision;
    }
    public void WaitForPendingLoads()
    { foreach (var task in _loads.Values) { try { task.GetAwaiter().GetResult().Dispose(); } catch { } } _loads.Clear(); }
    public void Dispose()
    { WaitForPendingLoads(); foreach (var source in _sources.Values) source.Dispose(); foreach (var instance in _instances.Values) instance.Dispose(); _sources.Clear(); _instances.Clear(); _copies.Dispose(); }
}
