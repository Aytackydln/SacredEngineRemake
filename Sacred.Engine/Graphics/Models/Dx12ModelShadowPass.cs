using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Records soft projected model silhouettes beneath world sprites and models.</summary>
internal sealed class Dx12ModelShadowPass
{
    private const float ShadowPainterDepth = 0.995f;

    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12ModelGeometryCache _geometryCache;
    private readonly Dx12ModelTextureCache _textureCache;
    private readonly GpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly int _fallbackTextureSlot;
    private readonly ModelRootConstantsUpdater _rootConstants = new(ModelShaderLayout.RootParameterCount);
    private readonly ModelShaderConstantsUpdater _shaderConstants = new();

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _directionalPipeline;
    private ID3D12PipelineState? _groundPipeline;

    public Dx12ModelShadowPass(
        ID3D12GraphicsCommandList commandList,
        Dx12ModelGeometryCache geometryCache,
        Dx12ModelTextureCache textureCache,
        GpuDescriptorHandle srvHeapStart,
        int descriptorSize,
        int fallbackTextureSlot)
    {
        _commandList = commandList;
        _geometryCache = geometryCache;
        _textureCache = textureCache;
        _srvHeapStart = srvHeapStart;
        _descriptorSize = descriptorSize;
        _fallbackTextureSlot = fallbackTextureSlot;
    }

    public void SetPipeline(
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState directionalPipeline,
        ID3D12PipelineState groundPipeline)
    {
        _rootSignature = rootSignature;
        _directionalPipeline = directionalPipeline;
        _groundPipeline = groundPipeline;
    }

    public void DisposePipeline()
    {
        _directionalPipeline?.Dispose();
        _directionalPipeline = null;
        _groundPipeline?.Dispose();
        _groundPipeline = null;
        _rootSignature = null;
    }

    public unsafe void Record(
        SacredCamera camera,
        IReadOnlyList<SceneModel> models,
        SceneLighting lighting,
        int frameIndex)
    {
        if (models.Count == 0 ||
            lighting.ShadowMode == SceneShadowMode.None ||
            lighting.ShadowOpacity <= 0.001f ||
            _rootSignature is null)
        {
            return;
        }

        if (lighting.ShadowMode == SceneShadowMode.SoftContact)
        {
            RecordGroundShadows(camera, models, lighting.ShadowOpacity);
            return;
        }

        if (lighting.DirectionToSun.Z <= 0.0f || _directionalPipeline is null)
            return;

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_directionalPipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        var viewProjection = camera.View * camera.Projection;
        var shadowParameters = PlanarShadowProjection.CreateParameters(
            lighting.DirectionToSun,
            lighting.ShadowOpacity);
        _rootConstants.Reset();
        foreach (var model in models)
            RecordModel(model, shadowParameters, viewProjection, frameIndex);
    }

    private unsafe void RecordGroundShadows(
        SacredCamera camera,
        IReadOnlyList<SceneModel> models,
        float opacity)
    {
        if (_groundPipeline is null || _rootSignature is null)
            return;

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_groundPipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _rootConstants.Reset();
        var viewProjection = camera.View * camera.Projection;
        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        foreach (var model in models)
        {
            var radius = model.GroundShadowRadius * model.Scale;
            var world = Matrix4x4.CreateScale(radius, radius, 1.0f) *
                        Matrix4x4.CreateTranslation(
                            model.Position.X,
                            model.Position.Y,
                            model.GroundPlaneZ);
            _shaderConstants.WriteModelBase(
                constants,
                world * viewProjection,
                world,
                new Vector4(0.0f, 0.0f, 0.0f, opacity));
            _shaderConstants.WriteTextureFlags(
                constants + ModelShaderLayout.TextureFlagsOffset,
                ModelShaderVariables.TextureModeNoTexture,
                animationValue: 0.0f,
                ShadowPainterDepth,
                scaledAnimationTime: 0.0f);
            SetConstants(constants);
            _commandList.DrawInstanced(6, 1, 0, 0);
        }
    }

    private unsafe void RecordModel(
        SceneModel model,
        Vector4 shadowParameters,
        Matrix4x4 viewProjection,
        int frameIndex)
    {
        if (model.Mesh.Vertices.Length == 0 || model.Mesh.Indices.Length == 0)
            return;

        var mesh = _geometryCache.GetOrCreate(model.Mesh, frameIndex);
        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        _shaderConstants.WriteModelBase(
            constants,
            viewProjection,
            model.Transform,
            shadowParameters);

        var vertexBufferView = mesh.VertexBufferViews[frameIndex];
        var indexBufferView = mesh.IndexBufferView;
        _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
        _commandList.IASetIndexBuffer(&indexBufferView);

        if (model.Mesh.Surfaces.Count == 0)
        {
            WriteMaterial(constants, hasTexture: false, model.GroundPlaneZ);
            SetConstants(constants);
            _commandList.SetGraphicsRootDescriptorTable(
                ModelShaderLayout.ModelTextureRootParameter,
                SrvGpuHandle(_fallbackTextureSlot));
            _commandList.DrawIndexedInstanced((uint)mesh.IndexCount, 1, 0, 0, 0);
            return;
        }

        foreach (var surface in model.Mesh.Surfaces)
        {
            if (surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                continue;

            var textureReference = model.ResolveTextureReference(surface.TextureName);
            var texture = _textureCache.Get(textureReference.TextureName);
            var hasTexture = texture is { Resource: not null, SrvSlot: >= 0 };
            WriteMaterial(constants, hasTexture, model.GroundPlaneZ);
            SetConstants(constants);
            _commandList.SetGraphicsRootDescriptorTable(
                ModelShaderLayout.ModelTextureRootParameter,
                SrvGpuHandle(hasTexture ? texture!.SrvSlot : _fallbackTextureSlot));
            var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
            _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
        }
    }

    private unsafe void WriteMaterial(float* constants, bool hasTexture, float groundPlaneZ) =>
        _shaderConstants.WriteTextureFlags(
            constants + ModelShaderLayout.TextureFlagsOffset,
            hasTexture ? ModelShaderVariables.TextureModeBaseTexture : ModelShaderVariables.TextureModeNoTexture,
            groundPlaneZ,
            ShadowPainterDepth,
            scaledAnimationTime: 0.0f);

    private unsafe void SetConstants(float* constants) =>
        _rootConstants.SetIfChanged(
            _commandList,
            ModelShaderLayout.ModelConstantsRootParameter,
            constants,
            ModelShaderLayout.ModelConstantsCount,
            0);

    private GpuDescriptorHandle SrvGpuHandle(int index) => _srvHeapStart + index * _descriptorSize;
}
