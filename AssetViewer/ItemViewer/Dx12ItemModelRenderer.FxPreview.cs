using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using AssetViewer.AssetViewer;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace AssetViewer.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
    private FxPreviewPlayback? _fxPreview;
    private ModelGpuMesh? _fxMesh;
    private ID3D12PipelineState _fxAlphaPipelineState = null!;
    private long _lastFxTimestamp;

    public void SetFxPreview(FxPreviewPlayback? preview)
    {
        WaitForGpu();
        _fxMesh?.Dispose();
        _fxMesh = null;
        _fxPreview = preview;
        if (preview is null) return;
        _fxMesh = UploadMesh(preview.Mesh);
        _lastFxTimestamp = Stopwatch.GetTimestamp();
        // Snapshot the filled effect's bounds; motion must not resize the camera each frame.
        foreach (var vertex in preview.Mesh.Vertices)
        {
            var extent = preview.IsPortal ? Vector3.Zero : new Vector3(new Vector2(vertex.Normal.X, vertex.Normal.Y).Length());
            _assetMin = Vector3.Min(_assetMin ?? (_mesh is null ? Vector3.Zero : _meshBounds.Min), vertex.Position - extent);
            _assetMax = Vector3.Max(_assetMax ?? (_mesh is null ? Vector3.Zero : _meshBounds.Max), vertex.Position + extent);
        }
        foreach (var point in preview.CameraFitPoints)
        {
            _assetMin = Vector3.Min(_assetMin ?? point, point);
            _assetMax = Vector3.Max(_assetMax ?? point, point);
        }
    }

    private void AdvanceFxPreview()
    {
        if (_fxPreview is null || _fxMesh is null) return;
        var now = Stopwatch.GetTimestamp();
        var delta = Math.Clamp((float)Stopwatch.GetElapsedTime(_lastFxTimestamp, now).TotalSeconds, 0, .1f);
        _lastFxTimestamp = now;
        if (_fxPreview.Playing) _fxPreview.Advance(delta);
        UpdateUploadBuffer(_fxMesh.VertexBuffer, MemoryMarshal.AsBytes(_fxPreview.Mesh.Vertices.AsSpan()));
    }

    private unsafe void RecordFxPreview()
    {
        if (_fxPreview is null || _fxMesh is null) return;
        var vertices = _fxMesh.VertexBufferView; var indices = _fxMesh.IndexBufferView;
        _commandList.SetDescriptorHeaps([_srvHeap]);
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.IASetVertexBuffers(0, 1, &vertices);
        _commandList.IASetIndexBuffer(&indices);
        var scene = stackalloc float[ModelShaderLayout.SceneConstantsCount];
        WritePreviewSceneConstants(scene);
        _commandList.SetGraphicsRoot32BitConstants(ModelShaderLayout.SceneConstantsRootParameter,
            ModelShaderLayout.SceneConstantsCount, scene, 0);
        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        var world = CreateWorldMatrix(); var projection = CreateViewProjectionMatrix();
        foreach (var draw in _fxPreview.Draws)
        {
            var texture = ResolveTexture(draw.TextureName);
            if (texture is null) continue;
            _commandList.SetPipelineState(draw.Additive ? _itemParticlePipelineState : _fxAlphaPipelineState);
            _modelShaderConstants.WriteModelBase(constants, projection, world, draw.Color);
            var flags = constants + ModelShaderLayout.TextureFlagsOffset;
            flags[0] = (float)draw.Mode;
            flags[1] = ModelShaderLayout.PreserveProjectedDepth;
            flags[2] = (draw.SourceColorOnly ? 0x10 : 0) | (draw.UsesCornerTint ? 0x200 : 0);
            flags[3] = (float)_fxPreview.Encoding(draw.TextureName);
            _commandList.SetGraphicsRoot32BitConstants(ModelShaderLayout.ModelConstantsRootParameter,
                ModelShaderLayout.ModelConstantsCount, constants, 0);
            _commandList.SetGraphicsRootDescriptorTable(ModelShaderLayout.ModelTextureRootParameter, SrvGpuHandle(texture.SrvSlot));
            var overlay = ResolveTexture(draw.OverlayTextureName);
            _commandList.SetGraphicsRootDescriptorTable(ModelShaderLayout.ModelOverlayTextureRootParameter,
                SrvGpuHandle(overlay?.SrvSlot ?? FallbackTextureSlot));
            _commandList.DrawIndexedInstanced((uint)draw.IndexCount, 1, (uint)draw.IndexStart, 0, 0);
        }
    }
}
