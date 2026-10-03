using System;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

/// <summary>Blends legacy sprite RGB and coverage before decoding the completed result to scRGB.</summary>
internal sealed class Dx12HdrArtComposition(Dx12DeviceContext graphics) : IDisposable
{
    private readonly ID3D12Resource?[] _snapshots = new ID3D12Resource?[Dx12DeviceContext.FrameCount];
    private ID3D12RootSignature? _root;
    private ID3D12PipelineState? _pipeline;
    private bool _active;

    public IDisposable? Begin(bool depth = true)
    {
        if (!graphics.IsHdrEnabled) return null;
        if (_active) throw new InvalidOperationException("HDR art composition cannot nest.");
        EnsurePipeline();
        _active = true;
        Transfer(1.0f / 2.2f, depth);
        return new Scope(this, depth);
    }

    private void End(bool depth)
    {
        Transfer(2.2f, depth);
        _active = false;
    }

    private void EnsurePipeline()
    {
        if (_pipeline is not null) return;
        var parameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, 2), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable
            {
                Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)]
            }, ShaderVisibility.Pixel)
        };
        var description = new RootSignatureDescription(RootSignatureFlags.None, parameters, []);
        _root = graphics.Device.CreateRootSignature(in description, RootSignatureVersion.Version1);
        _pipeline = graphics.Device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            RootSignature = _root,
            VertexShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.HdrArtTransferVertexShader),
            PixelShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.HdrArtTransferPixelShader),
            BlendState = Dx12BlendStates.Opaque,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [Format.R16G16B16A16_Float],
            SampleDescription = new SampleDescription(1, 0)
        });
        EngineLog.WriteLine("HDR sprite composition ready: authored RGB and opacity, linear HDR headroom.");
    }

    private void Transfer(float exponent, bool depth)
    {
        var target = graphics.SceneColor;
        var index = graphics.CurrentFrame.Index;
        var snapshot = _snapshots[index];
        var description = target.Description;
        if (snapshot is null || snapshot.Description.Width != description.Width ||
            snapshot.Description.Height != description.Height)
        {
            // AcquireFrame has waited for this frame's fence. Its texture and SRV
            // can be replaced without overwriting another frame's descriptors.
            snapshot?.Dispose();
            snapshot = graphics.Device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(description.Format, checked((uint)description.Width), description.Height),
                ResourceStates.PixelShaderResource);
            _snapshots[index] = snapshot;
            graphics.Device.CreateShaderResourceView(snapshot, null,
                graphics.SrvCpuHandle(Dx12DescriptorLayout.HdrArtSnapshot + index));
        }
        var commands = graphics.CommandList;
        commands.OMSetRenderTargets(graphics.SceneRenderTarget, null);
        Dx12TextureUploader.Transition(commands, target, ResourceStates.RenderTarget, ResourceStates.CopySource);
        Dx12TextureUploader.Transition(commands, snapshot, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
        commands.CopyResource(snapshot, target);
        Dx12TextureUploader.Transition(commands, snapshot, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        Dx12TextureUploader.Transition(commands, target, ResourceStates.CopySource, ResourceStates.RenderTarget);
        commands.SetGraphicsRootSignature(_root);
        commands.SetPipelineState(_pipeline);
        commands.SetGraphicsRoot32BitConstant(0, BitConverter.SingleToUInt32Bits(exponent), 0);
        commands.SetGraphicsRoot32BitConstant(0,
            BitConverter.SingleToUInt32Bits(graphics.DisplayProfile.ScenePaperWhiteNits / 80.0f), 1);
        commands.SetGraphicsRootDescriptorTable(1, graphics.SrvGpuHandle(Dx12DescriptorLayout.HdrArtSnapshot + index));
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.DrawInstanced(3, 1, 0, 0);
        commands.OMSetRenderTargets(graphics.SceneRenderTarget, depth ? graphics.DepthStencil : null);
    }

    public void Dispose()
    {
        foreach (var snapshot in _snapshots) snapshot?.Dispose();
        DisposePipeline();
    }

    public void DisposePipeline()
    {
        _pipeline?.Dispose();
        _root?.Dispose();
        _pipeline = null;
        _root = null;
    }

    private sealed class Scope(Dx12HdrArtComposition owner, bool depth) : IDisposable
    {
        private Dx12HdrArtComposition? _owner = owner;
        public void Dispose()
        {
            var current = _owner;
            _owner = null;
            current?.End(depth);
        }
    }
}

