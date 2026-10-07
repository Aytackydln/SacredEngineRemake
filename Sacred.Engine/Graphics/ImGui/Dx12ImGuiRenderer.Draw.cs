using System;
using ImGuiNET;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.ImGui;

internal sealed unsafe partial class Dx12ImGuiRenderer
{
    private static void CopyDrawData(ImDrawDataPtr drawData, ImGuiFrameResources resources)
    {
        var vertexOffset = 0;
        var indexOffset = 0;
        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists[listIndex];
            var vertexBytes = drawList.VtxBuffer.Size * VertexStride;
            var indexBytes = drawList.IdxBuffer.Size * IndexStride;
            Buffer.MemoryCopy(
                (void*)drawList.VtxBuffer.Data,
                (byte*)resources.VertexMapped + vertexOffset,
                resources.VertexCapacity - vertexOffset,
                vertexBytes);
            Buffer.MemoryCopy(
                (void*)drawList.IdxBuffer.Data,
                (byte*)resources.IndexMapped + indexOffset,
                resources.IndexCapacity - indexOffset,
                indexBytes);
            vertexOffset += vertexBytes;
            indexOffset += indexBytes;
        }
    }

    private void RecordDrawData(
        ImDrawDataPtr drawData,
        ImGuiFrameResources resources,
        float uiPaperWhiteNits,
        bool backgroundOnly,
        bool skipBackground)
    {
        if (_rootSignature is null || _pipeline is null)
            throw new InvalidOperationException("The ImGui DX12 pipeline has not been assigned.");

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        var constants = stackalloc float[ImGuiShaderLayout.ConstantsCount]
        {
            2.0f / drawData.DisplaySize.X,
            -2.0f / drawData.DisplaySize.Y,
            -1.0f - drawData.DisplayPos.X * (2.0f / drawData.DisplaySize.X),
            1.0f + drawData.DisplayPos.Y * (2.0f / drawData.DisplaySize.Y),
            uiPaperWhiteNits
        };
        _commandList.SetGraphicsRoot32BitConstants(
            ImGuiShaderLayout.ConstantsRootParameter,
            ImGuiShaderLayout.ConstantsCount,
            constants,
            0);

        var vertexView = new VertexBufferView(
            resources.VertexBuffer!.GPUVirtualAddress,
            (uint)(drawData.TotalVtxCount * VertexStride),
            VertexStride);
        var indexView = new IndexBufferView(
            resources.IndexBuffer!.GPUVirtualAddress,
            (uint)(drawData.TotalIdxCount * IndexStride),
            Format.R16_UInt);
        _commandList.IASetVertexBuffers(0, 1, &vertexView);
        _commandList.IASetIndexBuffer(&indexView);

        var globalVertexOffset = 0;
        var globalIndexOffset = 0;
        var clipOffset = drawData.DisplayPos;
        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists[listIndex];
            var isBackground = (nint)drawList.NativePtr == _backgroundDrawList;
            if ((backgroundOnly && !isBackground) || (skipBackground && isBackground))
            {
                globalIndexOffset += drawList.IdxBuffer.Size;
                globalVertexOffset += drawList.VtxBuffer.Size;
                continue;
            }
            for (var commandIndex = 0; commandIndex < drawList.CmdBuffer.Size; commandIndex++)
            {
                var command = drawList.CmdBuffer[commandIndex];
                if (command.UserCallback != 0)
                    continue;

                var clip = command.ClipRect;
                var left = Math.Max(0, (int)(clip.X - clipOffset.X));
                var top = Math.Max(0, (int)(clip.Y - clipOffset.Y));
                var right = Math.Min((int)drawData.DisplaySize.X, (int)(clip.Z - clipOffset.X));
                var bottom = Math.Min((int)drawData.DisplaySize.Y, (int)(clip.W - clipOffset.Y));
                if (right <= left || bottom <= top)
                    continue;

                _commandList.RSSetScissorRects(new RawRect(left, top, right, bottom));
                var textureSlot = command.TextureId == 0 ? _fontSrvSlot : checked((int)command.TextureId);
                _commandList.SetGraphicsRootDescriptorTable(
                    ImGuiShaderLayout.TextureRootParameter,
                    _srvHeapGpuStart + textureSlot * _srvDescriptorSize);
                _commandList.DrawIndexedInstanced(
                    command.ElemCount,
                    1,
                    (uint)(globalIndexOffset + command.IdxOffset),
                    globalVertexOffset + (int)command.VtxOffset,
                    0);
            }

            globalIndexOffset += drawList.IdxBuffer.Size;
            globalVertexOffset += drawList.VtxBuffer.Size;
        }
    }

}
