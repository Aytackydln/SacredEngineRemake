using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ImGuiNET;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Weapon;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Models;
using Sacred.Granny.Meshes;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Depth-tested inventory models in a transparent GPU atlas, independent of world resolution.</summary>
internal sealed class Dx12EquipmentPreviewPass : IDisposable
{
    private readonly AssetManager _assets;
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly EquipmentPreviewFrame[] _frames = [new(), new()];
    private readonly ID3D12DescriptorHeap _rtv;
    private readonly ID3D12DescriptorHeap _dsv;
    private readonly ModelShaderConstantsUpdater _constants = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private Dx12CreatedPipelineGroup? _pipeline;
    private Task<EquipmentPreviewAsset?[]>? _loading;
    private SacredEquipment[] _requested = [];
    private EquipmentPreviewAsset[] _loaded = [];
    private int _revision;

    public Dx12EquipmentPreviewPass(AssetManager assets, Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _assets = assets; _graphics = graphics; _uploader = uploader;
        _rtv = graphics.Device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, Dx12DeviceContext.FrameCount));
        _dsv = graphics.Device.CreateDescriptorHeap(new(DescriptorHeapType.DepthStencilView, Dx12DeviceContext.FrameCount));
    }

    public void Prepare(IEnumerable<SacredEquipment> equipment)
    {
        var requested = equipment.Where(e => !string.IsNullOrWhiteSpace(e.Item.ModelName))
            .DistinctBy(e => e.IdemId).OrderBy(e => e.IdemId).ToArray();
        if (!_requested.Select(e => e.IdemId).SequenceEqual(requested.Select(e => e.IdemId)))
        {
            _requested = requested; _loaded = []; _revision++;
            // AssetManager's model path is also used by background world requests;
            // keep its awaits off the game's synchronization context.
            _loading = Task.WhenAll(requested.Select(e => Task.Run(() => EquipmentPreviewAsset.LoadAsync(_assets, e))));
        }
        if (_loading is { IsCompletedSuccessfully: true })
        {
            _loaded = _loading.Result.OfType<EquipmentPreviewAsset>().ToArray();
            _loading = null; _revision++;
        }
        if (_loaded.Length == 0) return;
        _pipeline ??= Dx12RendererPipelineFactory.Create(_graphics.Device,
            Dx12PipelineFactory.Compile(Dx12PipelineCatalog.CreateModels(Dx12ShaderCatalog.Sdr,
                new() { IncludeDenseParticle = false, SamplerAddressMode = TextureAddressMode.Wrap }),
                Dx12ShaderCompiler.CompileShader), Format.R8G8B8A8_UNorm, Format.D32_Float);
        var frame = _frames[_graphics.CurrentFrame.Index];
        if (frame.Revision != _revision) Upload(frame);
        Record(frame);
    }

    public void Draw(ImDrawListPtr draw, SacredEquipment item, Vector2 origin, float scale, bool hand,
        float horizontalOffset = 0)
    {
        var frame = _frames[_graphics.CurrentFrame.Index];
        // Never show an old item's layer while the current loadout is loading.
        if (frame.Revision != _revision || !frame.Images.TryGetValue(item.IdemId, out var image)) return;
        var minimum = origin + (EquipmentPreviewAsset.Anchor(item, hand) + image.Asset.Minimum +
            new Vector2(horizontalOffset, 0)) * scale;
        draw.AddImage(FirstSlot, minimum, minimum + image.Asset.Size * scale,
            image.Position / frame.Size, (image.Position + image.Asset.Size) / frame.Size);
    }

    private int FirstSlot => Dx12DescriptorLayout.FirstEquipmentPreview +
        _graphics.CurrentFrame.Index * Dx12DescriptorLayout.EquipmentDescriptorsPerFrame;
    private CpuDescriptorHandle Rtv => _rtv.GetCPUDescriptorHandleForHeapStart() +
        _graphics.CurrentFrame.Index * (int)_graphics.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
    private CpuDescriptorHandle Dsv => _dsv.GetCPUDescriptorHandleForHeapStart() +
        _graphics.CurrentFrame.Index * (int)_graphics.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
    private GpuDescriptorHandle Gpu(int slot) => _graphics.SrvHeap.GetGPUDescriptorHandleForHeapStart() + slot * _graphics.SrvDescriptorSize;

    private void Upload(EquipmentPreviewFrame frame)
    {
        // BeginFrame has waited for this index's fence, so descriptors and resources can be replaced.
        frame.Dispose();
        var names = _loaded.SelectMany(a => a.Textures).DistinctBy(t => t.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length + 2 > Dx12DescriptorLayout.EquipmentDescriptorsPerFrame)
            throw new InvalidOperationException("Equipped item materials exceed the UI descriptor reservation.");
        var position = Vector2.Zero;
        var rowHeight = 0f;
        var maximum = Vector2.One;
        foreach (var asset in _loaded)
        {
            if (position.X + asset.Size.X > 2048 && position.X > 0)
            { position = new(0, position.Y + rowHeight); rowHeight = 0; }
            frame.Images.Add(asset.Equipment.IdemId, (position, asset));
            var vertices = _uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(asset.Mesh.Vertices.AsSpan()));
            var indices = _uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(asset.Mesh.Indices.AsSpan()));
            frame.Resources.Add(vertices); frame.Resources.Add(indices);
            frame.Draws.Add(new(asset, position,
                new(vertices.GPUVirtualAddress, (uint)vertices.Description.Width, (uint)Marshal.SizeOf<VertexPositionNormalTexture>()),
                new(indices.GPUVirtualAddress, (uint)indices.Description.Width, Format.R16_UInt)));
            maximum = Vector2.Max(maximum, position + asset.Size);
            rowHeight = MathF.Max(rowHeight, asset.Size.Y); position.X += asset.Size.X;
        }
        frame.Size = maximum;
        frame.Target = _graphics.Device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)maximum.X, (uint)maximum.Y,
                1, 1, 1, 0, ResourceFlags.AllowRenderTarget), ResourceStates.PixelShaderResource,
            new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0, 0, 0, 0)));
        frame.Depth = _graphics.Device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(Format.D32_Float, (uint)maximum.X, (uint)maximum.Y,
                1, 1, 1, 0, ResourceFlags.AllowDepthStencil), ResourceStates.DepthWrite,
            new ClearValue(Format.D32_Float, 1, 0));
        _graphics.Device.CreateRenderTargetView(frame.Target, null, Rtv);
        _graphics.Device.CreateDepthStencilView(frame.Depth, null, Dsv);
        _uploader.CreateShaderResourceView(frame.Target, _graphics.SrvCpuHandle(FirstSlot));
        UploadTexture("", 1, 1, [255, 255, 255, 255]);
        foreach (var (name, texture) in names) UploadTexture(name, texture.Width, texture.Height, texture.Rgba8);
        frame.Revision = _revision;

        void UploadTexture(string name, int width, int height, byte[] pixels)
        {
            var slot = FirstSlot + 1 + frame.Textures.Count;
            var texture = _uploader.UploadRgbaTexture(_graphics.CommandList, width, height, pixels,
                _graphics.CurrentFrame.TransientResources);
            frame.Resources.Add(texture);
            _uploader.CreateShaderResourceView(texture, _graphics.SrvCpuHandle(slot));
            frame.Textures.Add(name, (slot, SacredTextureChannelAnalyzer.Analyze(pixels) != SacredTextureChannelEncoding.Rgb));
        }
    }

    private unsafe void Record(EquipmentPreviewFrame frame)
    {
        var commands = _graphics.CommandList;
        Dx12TextureUploader.Transition(commands, frame.Target!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(Rtv, Dsv);
        commands.ClearRenderTargetView(Rtv, new Color4(0, 0, 0, 0));
        commands.ClearDepthStencilView(Dsv, ClearFlags.Depth, 1, 0);
        commands.SetDescriptorHeaps(1, _graphics.ShaderVisibleDescriptorHeaps);
        commands.SetGraphicsRootSignature(_pipeline!.RootSignature);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        var scene = stackalloc float[ModelShaderLayout.SceneConstantsCount];
        _constants.WriteSceneConstants(scene, Vector3.Normalize(new(-0.35f, 1.8f, 0.85f)), 0.10f,
            new(0, 600, 0), 16, new(1, 1, 1, 0.38f), new(1, 0.96f, 0.88f, 0.82f),
            new(203, 0, 300, 80), (float)Stopwatch.GetElapsedTime(_started).TotalSeconds);
        commands.SetGraphicsRoot32BitConstants(ModelShaderLayout.SceneConstantsRootParameter,
            ModelShaderLayout.SceneConstantsCount, scene, 0);
        commands.SetGraphicsRootDescriptorTable(ModelShaderLayout.SurfaceLightMapRootParameter, Gpu(FirstSlot + 1));
        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        foreach (var draw in frame.Draws)
        {
            var asset = draw.Asset;
            commands.RSSetViewports(new Viewport(draw.Position.X, draw.Position.Y, asset.Size.X, asset.Size.Y, 0, 1));
            commands.RSSetScissorRects(new RawRect((int)draw.Position.X, (int)draw.Position.Y,
                (int)(draw.Position.X + asset.Size.X), (int)(draw.Position.Y + asset.Size.Y)));
            var vertexView = draw.Vertices; var indexView = draw.Indices;
            commands.IASetVertexBuffers(0, 1, &vertexView); commands.IASetIndexBuffer(&indexView);
            var world = asset.Transform * Matrix4x4.CreateTranslation(-asset.Minimum.X - asset.Size.X / 2, 0,
                asset.Minimum.Y + asset.Size.Y / 2);
            var view = new Matrix4x4(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1) *
                Matrix4x4.CreateOrthographicLeftHanded(asset.Size.X, asset.Size.Y, -400, 2000);
            var color = ModelShaderVariables.ColorFromName(asset.Equipment.Item.ModelName);
            _constants.WriteModelBase(constants, world * view, world, color);
            for (var pass = ModelSurfacePass.Static; pass <= ModelSurfacePass.EffectOverlay; pass++)
            foreach (var surface in asset.Mesh.Surfaces)
            {
                var material = asset.Materials.GetValueOrDefault(surface.TextureName ?? "", ModelTextureReference.Static(""));
                var texture = frame.Textures.GetValueOrDefault(material.TextureName, frame.Textures[""]);
                var overlay = frame.Textures.GetValueOrDefault(material.OverlayTextureName ?? "", frame.Textures[""]);
                if (!ModelSurfacePassSelector.TrySelect(pass, material, material.Animation.IsAnimated,
                    material.OverlayAnimation.IsAnimated, true, material.HasOverlay, out var mode, out var animation, out _)) continue;
                var kind = pass switch
                {
                    ModelSurfacePass.AnimatedBase => Dx12PipelineKind.AnimatedModel,
                    ModelSurfacePass.EffectOverlay => Dx12PipelineKind.TransparentEffectModel,
                    _ when texture.Alpha => Dx12PipelineKind.TransparentModel,
                    _ => Dx12PipelineKind.SolidModel
                };
                commands.SetPipelineState(_pipeline[kind]);
                if (animation.Mode == TextureAnimationMode.RadialSweepBlackKey &&
                    MeshSurfaceRadialSweep.TryCalculate(asset.Mesh, surface, out var radial))
                    _constants.WriteModelColor(constants + 32, radial);
                else _constants.WriteModelColor(constants + 32, color);
                _constants.WriteTextureFlags(constants + ModelShaderLayout.TextureFlagsOffset, mode,
                    ModelShaderVariables.PackTextureAnimation(animation.IsAnimated,
                        animation.Mode == TextureAnimationMode.RadialSweepBlackKey, false),
                    ModelShaderLayout.PreserveProjectedDepth, animation.IsAnimated ? animation.TimeScale : 0);
                commands.SetGraphicsRoot32BitConstants(ModelShaderLayout.ModelConstantsRootParameter,
                    ModelShaderLayout.ModelConstantsCount, constants, 0);
                commands.SetGraphicsRootDescriptorTable(ModelShaderLayout.ModelTextureRootParameter, Gpu(texture.Slot));
                commands.SetGraphicsRootDescriptorTable(ModelShaderLayout.ModelOverlayTextureRootParameter, Gpu(overlay.Slot));
                commands.DrawIndexedInstanced((uint)surface.IndexCount, 1, (uint)surface.IndexStart, 0, 0);
            }
        }
        Dx12TextureUploader.Transition(commands, frame.Target!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
    }

    public void Dispose()
    {
        foreach (var frame in _frames) frame.Dispose();
        if (_pipeline is { } pipeline)
        {
            foreach (var state in pipeline.Pipelines.Values) state.Dispose();
            pipeline.RootSignature.Dispose();
        }
        _rtv.Dispose(); _dsv.Dispose();
    }
}
