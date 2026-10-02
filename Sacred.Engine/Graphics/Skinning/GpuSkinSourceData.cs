using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Engine.Graphics.Skinning;

// Runtime structured-buffer layouts. Matrices use System.Numerics row-vector order.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct GpuSkinVertex(Vector3 Position, Vector3 Normal, Vector2 Uv,
    uint InfluenceOffset, uint InfluenceCount, uint Rigid, uint Padding);
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct GpuSkinInfluence(int BoneIndex, float Weight);
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct GpuSkinProjection(Vector3 Offset, uint Horizontal0,
    uint Horizontal1, uint Vertical, uint BoneCount, uint VertexCount);

/// <summary>Render-thread snapshot; workers never read mutable mesh arrays or instance palettes.</summary>
internal sealed class GpuSkinSourceData
{
    public GpuSkinSourceData(Mesh mesh, GrnSkinningData skin)
    {
        if (mesh.Vertices.Length != skin.Vertices.Length) throw new ArgumentException("Skin vertex count mismatch.");
        var vertices = new GpuSkinVertex[skin.Vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = skin.Vertices[i];
            vertices[i] = new(v.BindPosition, v.BindNormal, mesh.Vertices[i].TexCoord,
                checked((uint)v.InfluenceOffset), checked((uint)v.InfluenceCount), v.UsesRigidBoneTransform ? 1u : 0u, 0);
        }
        // Empty buffers need physical storage, but vertex counts remain zero. Preserve every
        // raw influence, including invalid indices/weights, for identical shader filtering later.
        var influences = new GpuSkinInfluence[Math.Max(1, skin.Influences.Length)];
        for (var i = 0; i < skin.Influences.Length; i++)
            influences[i] = new(skin.Influences[i].BoneIndex, skin.Influences[i].Weight);
        if (skin.Influences.Length == 0) influences[0] = new(-1, 0);
        var p = skin.Projection;
        var projection = new GpuSkinProjection[] { new(p.Project(Vector3.Zero), (uint)p.HorizontalAxis0,
            (uint)p.HorizontalAxis1, (uint)p.VerticalAxis, (uint)skin.Bones.Length, (uint)vertices.Length) };
        Vertices = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray();
        Influences = MemoryMarshal.AsBytes(influences.AsSpan()).ToArray();
        Projection = MemoryMarshal.AsBytes(projection.AsSpan()).ToArray();
        Indices = MemoryMarshal.AsBytes(mesh.Indices.AsSpan()).ToArray();
        IndexCount = mesh.Indices.Length;
    }
    public byte[] Vertices { get; }
    public byte[] Influences { get; }
    public byte[] Projection { get; }
    public byte[] Indices { get; }
    public int IndexCount { get; }
}
