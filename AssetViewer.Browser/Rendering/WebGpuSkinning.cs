using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Granny.Animation;
using Sacred.Granny.Diagnostics;
using Sacred.Granny.Meshes;

namespace AssetViewer.Browser.Rendering;

/// <summary>Shared C# poses and the desktop's packed bind data; WebGPU only deforms the vertices.</summary>
internal sealed class WebGpuSkinning
{
    private readonly JSObject _host;
    private readonly Matrix4x4[] _palettes;
    private ulong _revision;

    internal WebGpuSkinning(JSObject host, Mesh mesh, GrnSkinningData data)
    {
        _host = host;
        _palettes = new Matrix4x4[Math.Max(1, data.Bones.Length) * 2];
        Array.Fill(_palettes, Matrix4x4.Identity);
        var source = new GpuSkinSourceData(mesh, data);
        WebGpuInterop.SetSkinning(host, source.Vertices, source.Influences, source.Projection,
            Math.Max(1, data.Bones.Length), data.Vertices.Length);
    }

    internal void Update(GrnPose pose)
    {
        if (!pose.IsEvaluated || _revision == pose.Revision) return;
        using var measurement = AnimationPerformance.Measure(AnimationCpuStage.PaletteUpload);
        var count = _palettes.Length / 2;
        pose.SkinTransforms.CopyTo(_palettes);
        pose.RigidSkinTransforms.CopyTo(_palettes.AsSpan(count));
        WebGpuInterop.SetSkinningPose(_host, MemoryMarshal.AsBytes(_palettes.AsSpan(0, count)),
            MemoryMarshal.AsBytes(_palettes.AsSpan(count)));
        AnimationPerformance.RecordPaletteUpload(_palettes.Length * 64);
        _revision = pose.Revision;
    }
}
