using System.Numerics;
using System.Runtime.InteropServices;
using AssetViewer.ItemViewer;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.Browser.Rendering;

internal sealed partial class WebGpuPreviewScene
{
    private void Draw(string mesh, string pipeline, string? texture, string? overlay, int start, int count,
        Matrix4x4 transform, Matrix4x4 world, Vector4 color, Vector4 flags)
    {
        Span<float> constants = stackalloc float[40];
        var bytes = MemoryMarshal.AsBytes(constants);
        MemoryMarshal.Write(bytes, in transform); MemoryMarshal.Write(bytes[64..], in world);
        MemoryMarshal.Write(bytes[128..], in color); MemoryMarshal.Write(bytes[144..], in flags);
        WebGpuInterop.Draw(host, mesh, pipeline, texture ?? "", overlay ?? "", TextureAnimation.MultitextureGlowName,
            start, count, bytes);
    }

    private void DrawModel(Matrix4x4 world, Matrix4x4 projection)
    {
        var surfaces = _mesh!.Surfaces.Count == 0 ? [new MeshSurface(0, _mesh.Indices.Length, null)] : _mesh.Surfaces;
        PreviewMaterialTexture? Resolve(string? name) => name is not null && _materials.TryGetValue(name, out var material) ? material : null;
        foreach (var draw in ModelPreviewMaterials.Enumerate(surfaces, _animated is null ? _mesh : null, _asset!.Name, Resolve,
                     _textures.ContainsKey(TextureAnimation.MultitextureGlowName) ? 0 : -1))
        {
            var surface = draw.Surface;
            if (surface.IndexCount <= 0 || surface.IndexStart >= _mesh.Indices.Length) continue;
            var color = draw.Color;
            if (_animated is not null && draw.Pass != PreviewMaterialPass.Static && Resolve(surface.TextureName) is { } texture)
            {
                var animation = draw.Pass == PreviewMaterialPass.EffectOverlay ? texture.OverlayAnimation : texture.Animation;
                if (animation.Mode == TextureAnimationMode.RadialSweepBlackKey && _animated.TryGetRadialSweep(surface, out var radial))
                { color = radial; if (animation.HasSecondaryGlow) color.W = draw.Color.W; }
            }
            Draw("model", draw.Pass switch { PreviewMaterialPass.AnimatedBase => "animated", PreviewMaterialPass.EffectOverlay => "effect", _ when draw.Translucent => "model", _ => "opaque" },
                surface.TextureName, draw.UsesOverlay ? OverlayKey(surface.TextureName!) : null,
                surface.IndexStart, Math.Min(surface.IndexCount, _mesh.Indices.Length - surface.IndexStart),
                world * projection, world, color, new(draw.TextureMode, draw.AnimationValue, -1, draw.TimeScale));
        }
    }

    private void DrawEquipment(Matrix4x4 world, Matrix4x4 projection, float elapsed)
    {
        if (_effects.Mesh is null) return;
        foreach (var surface in _effects.Surfaces)
        {
            if (!_textures.ContainsKey(surface.TextureName) || surface.IndexCount <= 0) continue;
            var effectWorld = world;
            if (surface.TextureMode == ParticleTextureMode.PoisonStatic && surface.MotionVector.LengthSquared() > .0001f)
                effectWorld = Matrix4x4.CreateTranslation(surface.MotionVector * ((elapsed * .42f + surface.Phase) % 1)) * world;
            Draw("equipment", ParticleShaderCatalog.ForMode(surface.TextureMode) == ParticleShaderKind.ItemGlow ? "glow" : "particle",
                surface.TextureName, null, surface.IndexStart, surface.IndexCount, projection, effectWorld, surface.Color,
                new((float)surface.TextureMode, -1, surface.Phase, 0));
        }
    }

    private void DrawFx(Matrix4x4 world, Matrix4x4 projection)
    {
        if (_fx is null) return;
        foreach (var draw in _fx.Draws)
        {
            if (!_textures.ContainsKey(draw.TextureName) && !draw.Untextured) continue;
            var flags = (draw.SourceColorOnly ? 0x10 : 0) | (draw.UsesCornerTint ? 0x200 : 0) | (draw.Untextured ? 0x400 : 0);
            Draw("fx", draw.LineStrip ? "line" : draw.UsesModelLighting ? "model" : draw.Additive ? "particle" : "alpha",
                draw.TextureName, draw.OverlayTextureName, draw.IndexStart, draw.IndexCount,
                draw.UsesModelLighting ? world * projection : projection, world, draw.Color,
                new((float)draw.Mode, -1, flags, (float)_fx.Encoding(draw.TextureName)));
        }
    }

    private void DrawInventory(Matrix4x4 projection)
    {
        var vertices = new List<VertexPositionNormalTexture>(); var indices = new List<ushort>();
        void Quad(float x0, float z0, float x1, float z1)
        {
            var start = (ushort)vertices.Count;
            vertices.Add(new(new(x0, 0, z1), -Vector3.UnitY, new(0, 0)));
            vertices.Add(new(new(x1, 0, z1), -Vector3.UnitY, new(1, 0)));
            vertices.Add(new(new(x1, 0, z0), -Vector3.UnitY, new(1, 1)));
            vertices.Add(new(new(x0, 0, z0), -Vector3.UnitY, new(0, 1)));
            indices.AddRange([start, (ushort)(start + 1), (ushort)(start + 2), start, (ushort)(start + 2), (ushort)(start + 3)]);
        }
        var center = InventoryCenter;
        Quad(center.X - _gridWidth * 16, center.Y - _gridHeight * 16, center.X + _gridWidth * 16, center.Y + _gridHeight * 16);
        for (var column = 0; column <= 4; column++) { var x = -64 + column * 32; Quad(x - .675f, -80, x + .675f, 80); }
        for (var row = 0; row <= 5; row++) { var z = -80 + row * 32; Quad(-64, z - .675f, 64, z + .675f); }
        var mesh = new Mesh(vertices.ToArray(), indices.ToArray()); Upload("ui", mesh);
        Draw("ui", "ui", null, null, 0, 6, projection, Matrix4x4.Identity, new(.4f,.4f,.4f,.2f), Vector4.Zero);
        Draw("ui", "ui", null, null, 6, indices.Count - 6, projection, Matrix4x4.Identity, new(.48f,.48f,0,.52f), Vector4.Zero);
    }
}
