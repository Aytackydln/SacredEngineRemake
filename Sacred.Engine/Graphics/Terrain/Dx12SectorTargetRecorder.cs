using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Records tile and embedded-sprite batches into a sector render target.</summary>
internal sealed class Dx12SectorTargetRecorder(
    ID3D12Device device,
    Dx12TextureUploader uploader,
    ID3D12RootSignature rootSignature,
    ID3D12PipelineState spritePipeline)
{
    private const int VerticesPerTile = 6;

    public unsafe void RecordTarget(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource target,
        CpuDescriptorHandle rtv,
        int width,
        int height,
        GpuTerrainTileDraw[] draws,
        GpuSectorSpriteDraw[] embeddedSprites,
        ID3D12PipelineState pipeline,
        ICollection<ID3D12Resource> transientResources,
        ID3D12DescriptorHeap sourceSrvHeap,
        ref int nextSourceDescriptor)
    {
        commandList.OMSetRenderTargets(rtv, null);
        commandList.ClearRenderTargetView(rtv, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        commandList.RSSetViewports(new Viewport(0, 0, width, height, 0.0f, 1.0f));
        commandList.RSSetScissorRects(new RawRect(0, 0, width, height));

        if (draws.Length != 0)
        {
            // Descriptors belong to this fence-protected context. Reuse a sheet's
            // descriptor across all tiles without changing their painter order.
            var descriptors = new Dictionary<SourceTexture, uint>();
            var sourceCpuStart = sourceSrvHeap.GetCPUDescriptorHandleForHeapStart();
            var sourceDescriptorSize = (int)device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            var nextDescriptor = nextSourceDescriptor;
            var instances = new GpuTerrainTileInstance[draws.Length];
            for (var index = 0; index < draws.Length; index++)
            {
                var draw = draws[index];
                instances[index] = draw.Instance;
                instances[index].PrimaryTextureIndex = GetDescriptor(draw.Primary);
                instances[index].SecondaryTextureIndex = GetDescriptor(draw.Secondary);
            }
            nextSourceDescriptor = nextDescriptor;

            uint GetDescriptor(SourceTexture source)
            {
                if (descriptors.TryGetValue(source, out var descriptor))
                    return descriptor;
                descriptor = checked((uint)nextDescriptor++);
                uploader.CreateShaderResourceView(
                    source.Resource, sourceCpuStart + (int)descriptor * sourceDescriptorSize);
                descriptors.Add(source, descriptor);
                return descriptor;
            }
            var instanceBytes = MemoryMarshal.AsBytes(instances.AsSpan());
            var instanceBuffer = uploader.CreateUploadBuffer(instanceBytes);
            transientResources.Add(instanceBuffer);
            commandList.SetDescriptorHeaps(1, [sourceSrvHeap]);
            commandList.SetGraphicsRootSignature(rootSignature);
            commandList.SetPipelineState(pipeline);
            commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            var targetSize = stackalloc float[2] { width, height };
            commandList.SetGraphicsRoot32BitConstants(0, 2, targetSize, 0);
            commandList.SetGraphicsRootShaderResourceView(1, instanceBuffer.GPUVirtualAddress);
            commandList.DrawInstanced(VerticesPerTile, (uint)draws.Length, 0, 0);
        }

        if (embeddedSprites.Length != 0)
        {
            var instances = new GpuSectorSpriteInstance[embeddedSprites.Length];
            for (var index = 0; index < embeddedSprites.Length; index++)
                instances[index] = embeddedSprites[index].Instance;
            var instanceBuffer = uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(instances.AsSpan()));
            transientResources.Add(instanceBuffer);
            commandList.SetDescriptorHeaps(1, [sourceSrvHeap]);
            commandList.SetGraphicsRootSignature(rootSignature);
            commandList.SetPipelineState(spritePipeline);
            commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            var targetSize = stackalloc float[2] { width, height };
            commandList.SetGraphicsRoot32BitConstants(0, 2, targetSize, 0);
            var sourceCpuStart = sourceSrvHeap.GetCPUDescriptorHandleForHeapStart();
            var sourceGpuStart = sourceSrvHeap.GetGPUDescriptorHandleForHeapStart();
            var sourceDescriptorSize = (int)device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            var instanceStride = Marshal.SizeOf<GpuSectorSpriteInstance>();
            var firstInstance = 0;
            while (firstInstance < embeddedSprites.Length)
            {
                var draw = embeddedSprites[firstInstance];
                var instanceCount = 1;
                while (firstInstance + instanceCount < embeddedSprites.Length &&
                       ReferenceEquals(draw.Source, embeddedSprites[firstInstance + instanceCount].Source))
                {
                    instanceCount++;
                }

                var sourceDescriptor = sourceCpuStart + nextSourceDescriptor * sourceDescriptorSize;
                uploader.CreateShaderResourceView(draw.Source.Resource, sourceDescriptor);
                uploader.CreateShaderResourceView(draw.Source.Resource, sourceDescriptor + sourceDescriptorSize);
                commandList.SetGraphicsRootDescriptorTable(
                    2,
                    sourceGpuStart + nextSourceDescriptor * sourceDescriptorSize);
                commandList.SetGraphicsRootShaderResourceView(
                    1,
                    instanceBuffer.GPUVirtualAddress + (ulong)(firstInstance * instanceStride));
                commandList.DrawInstanced(VerticesPerTile, (uint)instanceCount, 0, 0);
                nextSourceDescriptor += 2;
                firstInstance += instanceCount;
            }
        }

        Dx12TextureUploader.Transition(
            commandList,
            target,
            ResourceStates.RenderTarget,
            ResourceStates.PixelShaderResource);
    }

}

internal sealed record SourceTexture(ID3D12Resource Resource);

internal readonly record struct GpuTerrainTileDraw(
    GpuTerrainTileInstance Instance,
    SourceTexture Primary,
    SourceTexture Secondary);

internal readonly record struct GpuSectorSpriteDraw(
    GpuSectorSpriteInstance Instance,
    SourceTexture Source);

