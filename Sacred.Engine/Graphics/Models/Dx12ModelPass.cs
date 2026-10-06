using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Records the complete model pass using stable geometry and material caches.</summary>
internal sealed class Dx12ModelPass
{
    public Dx12GpuAnimationTimings? GpuTimings { get; set; }
    public Dx12SkinDrawBindings? SkinDraw { get; set; }
    public float? AnimationTimeOverride { get; set; }
    private const float PainterDepthScale = 1.0f / 4096.0f;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12ModelGeometryCache _geometryCache;
    private readonly Dx12ModelTextureCache _textureCache;
    private readonly GpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly int _fallbackTextureSlot;
    private readonly int _surfaceLightMapSlot;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly ModelRootConstantsUpdater _rootConstants = new(ModelShaderLayout.RootParameterCount);
    private readonly ModelDescriptorTableUpdater _descriptorTables = new(ModelShaderLayout.RootParameterCount);
    private readonly ModelShaderConstantsUpdater _shaderConstants = new();
    private readonly Dx12ModelShadowPass _shadowPass;
    private readonly Dx12EquipmentEffectPass _equipment;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _staticPipeline;
    private ID3D12PipelineState? _solidPipeline;
    private ID3D12PipelineState? _transparentModelPipeline;
    private ID3D12PipelineState? _animatedPipeline;
    private ID3D12PipelineState? _effectPipeline;
    private ID3D12PipelineState? _transparentEffectPipeline;


    public Dx12ModelPass(
        ID3D12GraphicsCommandList commandList,
        Dx12ModelGeometryCache geometryCache,
        Dx12ModelTextureCache textureCache,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int fallbackTextureSlot,
        int surfaceLightMapSlot)
    {
        _commandList = commandList;
        _geometryCache = geometryCache;
        _textureCache = textureCache;
        _srvHeapStart = srvHeap.GetGPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _fallbackTextureSlot = fallbackTextureSlot;
        _surfaceLightMapSlot = surfaceLightMapSlot;
        _equipment = new(commandList, geometryCache, textureCache, _srvHeapStart, descriptorSize, fallbackTextureSlot);
        _shadowPass = new Dx12ModelShadowPass(
            commandList,
            geometryCache,
            textureCache,
            _srvHeapStart,
            descriptorSize,
            fallbackTextureSlot);
    }

    public void SetPipeline(Dx12CreatedPipelineGroup pipeline, bool hdrOutput)
    {

        _rootSignature = pipeline.RootSignature;
        _shadowPass.SetPipeline(
            pipeline.RootSignature,
            pipeline[Dx12PipelineKind.ModelShadow],
            pipeline[Dx12PipelineKind.GroundShadow]);
        _staticPipeline = pipeline[Dx12PipelineKind.StaticModel];
        _solidPipeline = pipeline[Dx12PipelineKind.SolidModel];
        _transparentModelPipeline = pipeline[Dx12PipelineKind.TransparentModel];
        _animatedPipeline = pipeline[Dx12PipelineKind.AnimatedModel];
        _effectPipeline = pipeline[Dx12PipelineKind.EffectModel];
        _transparentEffectPipeline = pipeline[Dx12PipelineKind.TransparentEffectModel];
        _equipment.SetPipeline(pipeline, hdrOutput);
    }

    public void DisposePipeline()
    {
        _staticPipeline?.Dispose();
        _staticPipeline = null;
        _solidPipeline?.Dispose();
        _solidPipeline = null;
        _shadowPass.DisposePipeline();
        _transparentModelPipeline?.Dispose();
        _transparentModelPipeline = null;
        _animatedPipeline?.Dispose();
        _animatedPipeline = null;
        _effectPipeline?.Dispose();
        _effectPipeline = null;
        _transparentEffectPipeline?.Dispose();
        _transparentEffectPipeline = null;
        _equipment.DisposePipeline();
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public void RecordShadows(
        SacredCamera camera,
        IReadOnlyList<SceneModel> models,
        SceneLighting lighting,
        int frameIndex)
    {
        using var measurement = GpuTimings?.Measure(AnimationGpuStage.ModelShadows);
        _shadowPass.SkinDraw = SkinDraw;
        _shadowPass.GpuTimings = GpuTimings;
        _shadowPass.Record(camera, models, lighting, frameIndex);
    }

    public unsafe void Record(
        SacredCamera camera,
        IReadOnlyList<SceneModel> models,
        SceneLighting lighting,
        Dx12SceneColorProfile display,
        int frameIndex)
    {
        if (models.Count == 0 || _rootSignature is null || _staticPipeline is null)
            return;

        using var measurement = GpuTimings?.Measure(AnimationGpuStage.ModelDraws);

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_staticPipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _rootConstants.Reset();
        _descriptorTables.Reset();
        SetDescriptorTableIfChanged(
            ModelShaderLayout.SurfaceLightMapRootParameter,
            SrvGpuHandle(_surfaceLightMapSlot));

        var elapsedSeconds = AnimationTimeOverride ?? (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
        var sceneConstants = stackalloc float[ModelShaderLayout.SceneConstantsCount];
        WriteLighting(camera, lighting, display, elapsedSeconds, sceneConstants);
        SetRootConstantsIfChanged(
            ModelShaderLayout.SceneConstantsRootParameter,
            sceneConstants,
            ModelShaderLayout.SceneConstantsCount,
            0);

        SkinDraw?.UploadScene(new ReadOnlySpan<float>(sceneConstants, ModelShaderLayout.SceneConstantsCount), frameIndex);
        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        var viewProjection = camera.View * camera.Projection;
        foreach (var model in models)
        {
            if (!ModelFrustumCuller.IsVisible(camera, model))
                continue;

            var skinned = model.Geometry.Kind == SceneModelGeometryKind.GpuSkinned;
            var renderMesh = skinned ? model.Geometry.BindMesh : model.Mesh;
            if (renderMesh.Vertices.Length == 0 || renderMesh.Indices.Length == 0) continue;
            ModelGpuMesh? mesh = null;
            ModelGpuSkinSource? skinSource = null;
            ModelGpuSkinInstance? skinInstance = null;
            if (skinned)
            {
                if (SkinDraw is null || !SkinDraw.TryGet(model, frameIndex, out skinSource, out skinInstance)) continue;
            }
            else if (!_geometryCache.TryGetOrRequest(renderMesh, frameIndex, out mesh)) continue;
            _commandList.SetGraphicsRootSignature(skinned ? SkinDraw!.Root : _rootSignature);
            _commandList.SetPipelineState(skinned ? SkinDraw!.Pipeline(Dx12PipelineKind.StaticModel) : _staticPipeline);
            _rootConstants.Reset(); _descriptorTables.Reset();
            SetDescriptorTableIfChanged(ModelShaderLayout.SurfaceLightMapRootParameter, SrvGpuHandle(_surfaceLightMapSlot));
            if (skinned) SkinDraw!.Bind(_commandList, skinSource!, skinInstance!, frameIndex, scene: true);
            else SetRootConstantsIfChanged(ModelShaderLayout.SceneConstantsRootParameter, sceneConstants, ModelShaderLayout.SceneConstantsCount, 0);
            var indexCount = skinned ? skinSource!.IndexCount : mesh!.IndexCount;
            var skinMeasurement = skinned ? GpuTimings?.Measure(AnimationGpuStage.SkinnedModelDraws) : null;
            var world = model.Transform;
            var worldViewProjection = world * viewProjection;
            var modelSceneDepth = CalculateSceneDepth(camera, model);
            var modelGeometryDepth = ModelShaderVariables.EncodeFixedPainterDepth(modelSceneDepth);
            var defaultModelColor = ModelShaderVariables.ColorFromName(model.Name);
            _shaderConstants.WriteModelBase(
                constants,
                worldViewProjection,
                world,
                defaultModelColor);

            var vertexBufferView = skinned ? new VertexBufferView(skinSource!.Vertices.GPUVirtualAddress, (uint)skinSource.Vertices.Description.Width, 48) : mesh!.VertexBufferViews[frameIndex];
            var indexBufferView = skinned ? skinSource!.IndexView : mesh!.IndexBufferView;
            _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
            _commandList.IASetIndexBuffer(&indexBufferView);
            SetRootConstantsIfChanged(
                ModelShaderLayout.ModelConstantsRootParameter,
                constants,
                ModelShaderLayout.ModelBaseConstantsCount,
                ModelShaderLayout.ModelBaseConstantsOffset);

            if (renderMesh.Surfaces.Count == 0)
            {
                _commandList.SetPipelineState(skinned ? SkinDraw!.Pipeline(Dx12PipelineKind.SolidModel) : _solidPipeline!);
                RecordUntexturedMeshCount(indexCount, constants, modelGeometryDepth, model.IsFocused);
            }
            else
            {
                for (var passIndex = 0; passIndex < 3; passIndex++)
                {
                    var pass = (ModelSurfacePass)passIndex;
                    foreach (var surface in renderMesh.Surfaces)
                    {
                        if (surface.IndexCount <= 0 || surface.IndexStart >= indexCount)
                            continue;

                        var textureReference = model.ResolveTextureReference(surface.TextureName);
                        var animatesBase = textureReference.Animation.IsAnimated;
                        var animatesOverlay = textureReference.HasOverlay && textureReference.OverlayAnimation.IsAnimated;
                        var drawCount = Math.Min(surface.IndexCount, indexCount - surface.IndexStart);
                        var texture = _textureCache.Get(textureReference.TextureName);
                        var hasTexture = texture is { Resource: not null, SrvSlot: >= 0 };
                        Dx12ModelTextureCache.ModelTexture? overlayTexture = null;
                        var hasOverlayResource = false;
                        if (textureReference.HasOverlay)
                        {
                            overlayTexture = _textureCache.Get(textureReference.OverlayTextureName);
                            hasOverlayResource = overlayTexture is { Resource: not null, SrvSlot: >= 0 } &&
                                                 textureReference.OverlayMode != TextureOverlayMode.None;
                        }

                        if (!ModelSurfacePassSelector.TrySelect(
                                pass,
                                textureReference,
                                animatesBase,
                                animatesOverlay,
                                hasTexture,
                                hasOverlayResource,
                                out var textureMode,
                                out var animation,
                                out var hasOverlay))
                            continue;

                        var pipelineKind = pass switch
                        {
                            ModelSurfacePass.AnimatedBase => Dx12PipelineKind.AnimatedModel,
                            ModelSurfacePass.EffectOverlay when textureReference.OverlayCompositesInFront => Dx12PipelineKind.TransparentEffectModel,
                            ModelSurfacePass.EffectOverlay => Dx12PipelineKind.EffectModel,
                            _ when texture?.HasTranslucentPixels == true => Dx12PipelineKind.TransparentModel,
                            _ when !hasOverlay && (!hasTexture || texture?.IsFullyOpaque == true) => Dx12PipelineKind.SolidModel,
                            _ => Dx12PipelineKind.StaticModel
                        };
                        _commandList.SetPipelineState(skinned ? SkinDraw!.Pipeline(pipelineKind) : pipelineKind switch {
                            Dx12PipelineKind.AnimatedModel => _animatedPipeline!, Dx12PipelineKind.EffectModel => _effectPipeline!,
                            Dx12PipelineKind.TransparentEffectModel => _transparentEffectPipeline!, Dx12PipelineKind.TransparentModel => _transparentModelPipeline!,
                            Dx12PipelineKind.SolidModel => _solidPipeline!, _ => _staticPipeline });

                        var modelColor = animation.Mode == TextureAnimationMode.RadialSweepBlackKey &&
                                         model.Geometry.TryGetRadialSweep(surface, out var radialSweep)
                            ? radialSweep
                            : defaultModelColor;
                        _shaderConstants.WriteModelColor(constants + 32, modelColor);
                        SetRootConstantsIfChanged(
                            ModelShaderLayout.ModelConstantsRootParameter,
                            constants + 32,
                            4,
                            32);

                        _shaderConstants.WriteTextureFlags(
                            constants + ModelShaderLayout.TextureFlagsOffset,
                            ModelShaderVariables.PackWorldFocus(textureMode, model.IsFocused),
                            ModelShaderVariables.PackTextureAnimation(
                                animation.IsAnimated,
                                animation.Mode == TextureAnimationMode.RadialSweepBlackKey,
                                overlay: false),
                            modelGeometryDepth,
                            animation.IsAnimated ? animation.TimeScale : 0.0f);
                        SetRootConstantsIfChanged(
                            ModelShaderLayout.ModelConstantsRootParameter,
                            constants + ModelShaderLayout.TextureFlagsOffset,
                            ModelShaderLayout.TextureFlagsConstantsCount,
                            ModelShaderLayout.TextureFlagsOffset);
                        SetDescriptorTableIfChanged(
                            ModelShaderLayout.ModelTextureRootParameter,
                            SrvGpuHandle(hasTexture ? texture!.SrvSlot : _fallbackTextureSlot));
                        SetDescriptorTableIfChanged(
                            ModelShaderLayout.ModelOverlayTextureRootParameter,
                            SrvGpuHandle(hasOverlay ? overlayTexture!.SrvSlot : _fallbackTextureSlot));
                        _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
                    }
                }
            }

            skinMeasurement?.Dispose();
            _commandList.SetGraphicsRootSignature(_rootSignature);
            _rootConstants.Reset(); _descriptorTables.Reset();
            SetRootConstantsIfChanged(ModelShaderLayout.SceneConstantsRootParameter, sceneConstants, ModelShaderLayout.SceneConstantsCount, 0);
            RecordEquipmentEffects(model, viewProjection, modelSceneDepth, frameIndex, constants);
        }
    }

    private unsafe void RecordEquipmentEffects(SceneModel model, Matrix4x4 viewProjection, float modelSceneDepth, int frameIndex, float* constants) =>
        _equipment.Record(model, viewProjection, modelSceneDepth, frameIndex, constants);

    private unsafe void RecordUntexturedMesh(ModelGpuMesh mesh, float* constants, float modelSceneDepth) =>
        RecordUntexturedMeshCount(mesh.IndexCount, constants, modelSceneDepth);

    private unsafe void RecordUntexturedMeshCount(int indexCount, float* constants, float modelSceneDepth, bool focused = false)
    {
        _shaderConstants.WriteTextureFlags(
            constants + ModelShaderLayout.TextureFlagsOffset,
            ModelShaderVariables.PackWorldFocus(ModelShaderVariables.TextureModeNoTexture, focused),
            ModelShaderVariables.TextureAnimationNone,
            modelSceneDepth,
            animationTimeScale: 0.0f);
        SetRootConstantsIfChanged(
            ModelShaderLayout.ModelConstantsRootParameter,
            constants + ModelShaderLayout.TextureFlagsOffset,
            ModelShaderLayout.TextureFlagsConstantsCount,
            ModelShaderLayout.TextureFlagsOffset);
        var fallback = SrvGpuHandle(_fallbackTextureSlot);
        SetDescriptorTableIfChanged(ModelShaderLayout.ModelTextureRootParameter, fallback);
        SetDescriptorTableIfChanged(ModelShaderLayout.ModelOverlayTextureRootParameter, fallback);
        _commandList.DrawIndexedInstanced((uint)indexCount, 1, 0, 0, 0);
    }

    private unsafe void SetRootConstantsIfChanged(int parameter, float* constants, int count, int offset) =>
        _rootConstants.SetIfChanged(_commandList, parameter, constants, count, offset);

    private void SetDescriptorTableIfChanged(int parameter, GpuDescriptorHandle handle) =>
        _descriptorTables.SetIfChanged(_commandList, parameter, handle);

    private unsafe void WriteLighting(
        SacredCamera camera,
        SceneLighting lighting,
        Dx12SceneColorProfile display,
        float elapsedSeconds,
        float* target)
    {
        var lightDirection = lighting.DirectionToLight.LengthSquared() > float.Epsilon
            ? Vector3.Normalize(lighting.DirectionToLight)
            : Vector3.UnitZ;
        _shaderConstants.WriteSceneConstants(
            target,
            lightDirection,
            lighting.SpecularIntensity,
            camera.EyePosition,
            lighting.Shininess,
            new Vector4(lighting.AmbientColor, lighting.AmbientIntensity),
            new Vector4(lighting.LightColor, lighting.DiffuseIntensity),
            new Vector4(
                display.SceneWhiteScale,
                1.0f,
                display.ParticleColorMultiplier,
                display.UnlitSpriteScale),
            elapsedSeconds);
    }

    private static float CalculateSceneDepth(SacredCamera camera, SceneModel model)
    {
        // Submission order still uses the gameplay anchor. Physical depth must include
        // the elevated mesh origin so the selected floor cannot occlude its actor.
        var depthKey = WorldModelDepth.FromPosition(model.Transform.Translation);
        var centerDepthKey = WorldPainterDepth.FromWorld(camera.ViewCenter);
        var painterDepth = Math.Clamp(
            0.50f - (depthKey - centerDepthKey) * PainterDepthScale,
            0.20f,
            0.72f);
        return painterDepth;
    }

    private GpuDescriptorHandle SrvGpuHandle(int index) => _srvHeapStart + index * _descriptorSize;

}
