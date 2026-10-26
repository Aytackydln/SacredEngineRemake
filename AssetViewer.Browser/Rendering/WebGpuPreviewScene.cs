using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using AssetViewer.AssetViewer;
using AssetViewer.ItemViewer;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Diagnostics;
using Sacred.Granny.Meshes;

namespace AssetViewer.Browser.Rendering;

/// <summary>Browser draw submission. Decoding, poses and all FX simulation stay in shared C#.</summary>
internal sealed partial class WebGpuPreviewScene(JSObject host)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _previous = Stopwatch.GetTimestamp();
    private GrnAsset? _asset;
    private GrnAnimatedMesh? _animated;
    private WebGpuSkinning? _skinning;
    internal bool IsEmpty => _mesh is null && _fx is null;
    private EquipmentEffectScene _effects = EquipmentEffectScene.Empty;
    private FxPreviewPlayback? _fx;
    private IReadOnlyDictionary<string, ModelTextureBinding> _textures = new Dictionary<string, ModelTextureBinding>();
    private readonly Dictionary<string, PreviewMaterialTexture> _materials = new(StringComparer.OrdinalIgnoreCase);
    private Mesh? _mesh;
    private Vector3 _min, _max, _fitMin, _fitMax;
    private (Vector3 Min, Vector3 Max)? _frame;
    private Vector3 _rotation, _offset;
    private float _scale = 1;
    private float _radius = 1;
    private int _gridWidth = 1, _gridHeight = 1;
    private float _animationTime;
    internal bool AssetPreview { get; set; }
    internal bool GroundGrid { get; set; }
    internal bool AnimationPlaying { get; set; } = true;
    internal float Yaw { get; set; }
    internal float Pitch { get; set; }
    internal float Roll { get; set; }
    internal float Zoom { get; set; } = 1.12f;
    internal Vector2? SlotSize { get; set; }
    internal Vector2 SlotCenter { get; set; }

    internal void Clear()
    {
        _asset = null; _mesh = null; _animated = null; _skinning = null; _fx = null;
        _effects = EquipmentEffectScene.Empty; _textures = new Dictionary<string, ModelTextureBinding>();
        _materials.Clear(); _radius = 1;
        _min = _max = _fitMin = _fitMax = Vector3.Zero; _frame = null;
        WebGpuInterop.ClearResources(host);
    }

    internal void SetModel(GrnAsset asset, Vector3 rotation, int width, int height, EquipmentEffectScene effects)
    {
        Clear();
        _asset = asset;
        _rotation = float.IsFinite(rotation.X) && float.IsFinite(rotation.Y) && float.IsFinite(rotation.Z) ? rotation : Vector3.Zero;
        _gridWidth = Math.Clamp(width, 1, 4); _gridHeight = Math.Clamp(height, 1, 5);
        _effects = effects; _animationTime = 0; _previous = Stopwatch.GetTimestamp();
        _mesh = asset.Mesh;
        if (_mesh is null || _mesh.Vertices.Length == 0 || _mesh.Indices.Length == 0) return;
        (_min, _max) = Bounds(_mesh);
        var center = (_min + _max) * .5f;
        _radius = Math.Max(1, _mesh.Vertices.Max(vertex => Vector3.Distance(vertex.Position, center)));
        _fitMin = _min; _fitMax = _max;
        if (AssetPreview && asset.Skin is { } skin && asset.DefaultAnimation is { } clip)
        {
            _animated = new GrnAnimatedMesh(_mesh, skin, clip) { MaterializeCpuVertices = false };
            _animated.RequestPose(0);
            effects.ApplyPose(_animated);
        }
        Upload("model", _mesh);
        if (_animated is not null)
        {
            _skinning = new WebGpuSkinning(host, _mesh, _animated.Pose.Data);
            _skinning.Update(_animated.Pose);
        }
        if (effects.Mesh is { Vertices.Length: > 0, Indices.Length: > 0 } effectMesh)
        {
            Upload("equipment", effectMesh);
            foreach (var vertex in effectMesh.Vertices)
            {
                var extent = new Vector3(vertex.Normal.Z > .5f ? new Vector2(vertex.Normal.X, vertex.Normal.Y).Length() : 0);
                Include(vertex.Position, extent);
            }
        }
        Console.WriteLine($"[WebGPU] Model ready: {asset.Name}; {_mesh.Vertices.Length} vertices; {_mesh.Indices.Length / 3} triangles.");
    }

    internal void SetFrame(GrnAsset? asset) => _frame = asset?.Mesh is { } mesh ? Bounds(mesh) : null;
    internal void SetPlacement(float scale, Vector3 offset) { _scale = scale; _offset = offset; }
    internal void SetFx(FxPreviewPlayback? fx)
    {
        _fx = fx; _previous = Stopwatch.GetTimestamp();
        if (fx is null) return;
        Upload("fx", fx.Mesh);
        for (var i = 0; i < fx.Mesh.Vertices.Length; i++) Include(fx.Mesh.Vertices[i].Position, fx.VertexExtent(i));
        foreach (var point in fx.CameraFitPoints) Include(point, Vector3.Zero);
    }

    internal void SetTime(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _animationTime = seconds;
        if (_animated is not null)
        {
            _animated.RequestPose(seconds);
            _skinning?.Update(_animated.Pose);
            _effects.ResetNativeEffects();
            for (var i = 0; i < 120; i++) _effects.ApplyPose(_animated, 1f / 60);
        }
        _previous = Stopwatch.GetTimestamp();
        Console.WriteLine($"[WebGPU] Animation time: {seconds:F3}s.");
    }

    internal void SetTextures(IReadOnlyDictionary<string, ModelTextureBinding> textures)
    {
        _materials.Clear();
        foreach (var (name, binding) in textures)
        {
            var texture = binding.BaseTexture;
            var identity = WebGpuTextureIdentity.For(texture.Rgba8);
            WebGpuInterop.SetTexture(host, name, texture.Width, texture.Height, texture.Rgba8, identity.Key);
            if (binding.OverlayTexture is { } overlay)
                WebGpuInterop.SetTexture(host, OverlayKey(name), overlay.Width, overlay.Height, overlay.Rgba8,
                    WebGpuTextureIdentity.For(overlay.Rgba8).Key);
            _materials[name] = new(texture.Animation, binding.OverlayTexture?.Animation ?? TextureAnimation.None,
                binding.OverlayMode, binding.OverlayTexture is not null, identity.Translucent);
        }
        _textures = textures;
    }

    internal void Render(int width, int height)
    {
        using var frameMeasurement = AnimationPerformance.Measure(AnimationCpuStage.CharacterBatchWall);
        var now = Stopwatch.GetTimestamp();
        var delta = Math.Clamp((float)Stopwatch.GetElapsedTime(_previous, now).TotalSeconds, 0, .1f);
        _previous = now;
        if (_animated is not null && AnimationPlaying)
        {
            _animationTime += delta; _animated.RequestPose(_animationTime);
            _skinning?.Update(_animated.Pose);
        }
        if (_effects.Mesh is { Vertices.Length: > 0, Indices.Length: > 0 } equipment)
        {
            if (_animated is not null) _effects.ApplyPose(_animated, delta); else _effects.Advance(delta);
            Upload("equipment", equipment, false);
        }
        if (_fx is not null)
        {
            if (_fx.Playing) _fx.Advance(delta);
            Upload("fx", _fx.Mesh);
        }
        var aspect = Math.Max(.1f, width / (float)Math.Max(1, height));
        var world = World(); var projection = Projection(aspect);
        var elapsed = (float)Stopwatch.GetElapsedTime(_started, now).TotalSeconds;
        Span<float> scene = stackalloc float[24];
        WriteScene(scene, aspect, elapsed);
        WebGpuInterop.BeginFrame(host, width, height, MemoryMarshal.AsBytes(scene), SlotSize is not null);
        if (GroundGrid && AssetPreview && (_mesh is not null || _fx is not null)) DrawGrid(world, projection);
        if (!AssetPreview && SlotSize is null) DrawInventory(projection);
        if (_mesh is not null) DrawModel(world, projection);
        DrawEquipment(world, projection, elapsed);
        DrawFx(world, projection);
        WebGpuInterop.EndFrame(host);
    }

    private void Upload(string key, Mesh mesh, bool indices = true)
    {
        using var measurement = AnimationPerformance.Measure(AnimationCpuStage.VertexUpload);
        var vertices = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
        AnimationPerformance.RecordVertexUpload(vertices.Length);
        WebGpuInterop.SetMesh(host, key, vertices, MemoryMarshal.AsBytes(mesh.Indices.AsSpan()), indices);
    }

    private static (Vector3 Min, Vector3 Max) Bounds(Mesh mesh)
    {
        if (mesh.Vertices.Length == 0) return (Vector3.Zero, Vector3.Zero);
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var vertex in mesh.Vertices) { min = Vector3.Min(min, vertex.Position); max = Vector3.Max(max, vertex.Position); }
        return (min, max);
    }
    private void Include(Vector3 point, Vector3 extent)
    { _fitMin = Vector3.Min(_fitMin, point - extent); _fitMax = Vector3.Max(_fitMax, point + extent); }
    private static string OverlayKey(string name) => name + "#overlay";
}
