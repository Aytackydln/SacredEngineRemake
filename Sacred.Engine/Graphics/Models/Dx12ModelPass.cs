using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Meshes;
using Sacred.Particles;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Records the complete model pass using stable geometry and material caches.</summary>
internal sealed class Dx12ModelPass
{
    private const float PainterDepthScale = 1.0f / 4096.0f;
    private const float PlayerDepthBias = 0.0005f;

    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12ModelGeometryCache _geometryCache;
    private readonly Dx12ModelTextureCache _textureCache;
    private readonly GpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly int _fallbackTextureSlot;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly ModelRootConstantsUpdater _rootConstants = new(ModelShaderLayout.RootParameterCount);
    private readonly ModelShaderConstantsUpdater _shaderConstants = new();
    private readonly Dx12ModelShadowPass _shadowPass;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _staticPipeline;
    private ID3D12PipelineState? _transparentModelPipeline;
    private ID3D12PipelineState? _animatedPipeline;
    private ID3D12PipelineState? _effectPipeline;
    private ID3D12PipelineState? _transparentEffectPipeline;
    private ID3D12PipelineState? _transparentParticlePipeline;
    private ID3D12PipelineState? _denseParticlePipeline;
    private ID3D12PipelineState? _itemGlowPipeline;

    public Dx12ModelPass(
        ID3D12GraphicsCommandList commandList,
        Dx12ModelGeometryCache geometryCache,
        Dx12ModelTextureCache textureCache,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int fallbackTextureSlot)
    {
        _commandList = commandList;
        _geometryCache = geometryCache;
        _textureCache = textureCache;
        _srvHeapStart = srvHeap.GetGPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _fallbackTextureSlot = fallbackTextureSlot;
        _shadowPass = new Dx12ModelShadowPass(
            commandList,
            geometryCache,
            textureCache,
            _srvHeapStart,
            descriptorSize,
            fallbackTextureSlot);
    }

    public void SetPipeline(Dx12CreatedPipelineGroup pipeline)
    {
        _rootSignature = pipeline.RootSignature;
        _shadowPass.SetPipeline(
            pipeline.RootSignature,
            pipeline[Dx12PipelineKind.ModelShadow],
            pipeline[Dx12PipelineKind.GroundShadow]);
        _staticPipeline = pipeline[Dx12PipelineKind.StaticModel];
        _transparentModelPipeline = pipeline[Dx12PipelineKind.TransparentModel];
        _animatedPipeline = pipeline[Dx12PipelineKind.AnimatedModel];
        _effectPipeline = pipeline[Dx12PipelineKind.EffectModel];
        _transparentEffectPipeline = pipeline[Dx12PipelineKind.TransparentEffectModel];
        _transparentParticlePipeline = pipeline[Dx12PipelineKind.TransparentItemParticle];
        _denseParticlePipeline = pipeline[Dx12PipelineKind.DenseItemParticle];
        _itemGlowPipeline = pipeline[Dx12PipelineKind.ItemGlow];
    }

    public void DisposePipeline()
    {
        _staticPipeline?.Dispose();
        _staticPipeline = null;
        _shadowPass.DisposePipeline();
        _transparentModelPipeline?.Dispose();
        _transparentModelPipeline = null;
        _animatedPipeline?.Dispose();
        _animatedPipeline = null;
        _effectPipeline?.Dispose();
        _effectPipeline = null;
        _transparentEffectPipeline?.Dispose();
        _transparentEffectPipeline = null;
        _transparentParticlePipeline?.Dispose();
        _transparentParticlePipeline = null;
        _denseParticlePipeline?.Dispose();
        _denseParticlePipeline = null;
        _itemGlowPipeline?.Dispose();
        _itemGlowPipeline = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public void RecordShadows(
        SacredCamera camera,
        IReadOnlyList<SceneModel> models,
        SceneLighting lighting,
        int frameIndex) =>
        _shadowPass.Record(camera, models, lighting, frameIndex);

    public unsafe void Record(
        SacredCamera camera,
        IReadOnlyList<SceneModel> models,
        SceneLighting lighting,
        Dx12DisplayProfile display,
        int frameIndex)
    {
        if (models.Count == 0 || _rootSignature is null || _staticPipeline is null)
            return;

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_staticPipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _rootConstants.Reset();

        var sceneConstants = stackalloc float[ModelShaderLayout.SceneConstantsCount];
        WriteLighting(camera, lighting, display, sceneConstants);
        SetRootConstantsIfChanged(
            ModelShaderLayout.SceneConstantsRootParameter,
            sceneConstants,
            ModelShaderLayout.SceneConstantsCount,
            0);

        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        var elapsedSeconds = (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
        var viewProjection = camera.View * camera.Projection;
        foreach (var model in models)
        {
            if (model.Mesh.Vertices.Length == 0 || model.Mesh.Indices.Length == 0)
                continue;

            if (!_geometryCache.TryGetOrRequest(model.Mesh, frameIndex, out var mesh))
                continue;
            var world = model.Transform;
            var worldViewProjection = world * viewProjection;
            var modelSceneDepth = CalculateSceneDepth(camera, model);
            var defaultModelColor = ModelShaderVariables.ColorFromName(model.Name);
            _shaderConstants.WriteModelBase(
                constants,
                worldViewProjection,
                world,
                defaultModelColor);

            var vertexBufferView = mesh.VertexBufferViews[frameIndex];
            var indexBufferView = mesh.IndexBufferView;
            _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
            _commandList.IASetIndexBuffer(&indexBufferView);
            SetRootConstantsIfChanged(
                ModelShaderLayout.ModelConstantsRootParameter,
                constants,
                ModelShaderLayout.ModelBaseConstantsCount,
                ModelShaderLayout.ModelBaseConstantsOffset);

            if (model.Mesh.Surfaces.Count == 0)
            {
                RecordUntexturedMesh(mesh, constants, modelSceneDepth);
            }
            else
            {
                for (var passIndex = 0; passIndex < 3; passIndex++)
                {
                    var pass = (ModelSurfacePass)passIndex;
                    foreach (var surface in model.Mesh.Surfaces)
                    {
                        if (surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                            continue;

                        var textureReference = model.ResolveTextureReference(surface.TextureName);
                        var animatesBase = textureReference.Animation.IsAnimated;
                        var animatesOverlay = textureReference.HasOverlay && textureReference.OverlayAnimation.IsAnimated;
                        var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
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

                        _commandList.SetPipelineState(pass switch
                        {
                            ModelSurfacePass.AnimatedBase => _animatedPipeline!,
                            ModelSurfacePass.EffectOverlay when textureReference.OverlayCompositesInFront => _transparentEffectPipeline!,
                            ModelSurfacePass.EffectOverlay => _effectPipeline!,
                            _ when texture?.HasTranslucentPixels == true => _transparentModelPipeline!,
                            _ => _staticPipeline
                        });

                        var modelColor = animation.Mode == TextureAnimationMode.RadialSweepBlackKey &&
                                         MeshSurfaceRadialSweep.TryCalculate(model.Mesh, surface, out var radialSweep)
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
                            textureMode,
                            ModelShaderVariables.PackTextureAnimation(
                                animation.IsAnimated,
                                animation.Mode == TextureAnimationMode.RadialSweepBlackKey,
                                overlay: false),
                            modelSceneDepth,
                            animation.IsAnimated ? elapsedSeconds * animation.TimeScale : 0.0f);
                        SetRootConstantsIfChanged(
                            ModelShaderLayout.ModelConstantsRootParameter,
                            constants + ModelShaderLayout.TextureFlagsOffset,
                            ModelShaderLayout.TextureFlagsConstantsCount,
                            ModelShaderLayout.TextureFlagsOffset);
                        _commandList.SetGraphicsRootDescriptorTable(
                            ModelShaderLayout.ModelTextureRootParameter,
                            SrvGpuHandle(hasTexture ? texture!.SrvSlot : _fallbackTextureSlot));
                        _commandList.SetGraphicsRootDescriptorTable(
                            ModelShaderLayout.ModelOverlayTextureRootParameter,
                            SrvGpuHandle(hasOverlay ? overlayTexture!.SrvSlot : _fallbackTextureSlot));
                        _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
                    }
                }
            }

            RecordEquipmentEffects(model, viewProjection, modelSceneDepth, elapsedSeconds, frameIndex, constants);
        }
    }

    private unsafe void RecordEquipmentEffects(
        SceneModel model,
        Matrix4x4 viewProjection,
        float modelSceneDepth,
        float elapsedSeconds,
        int frameIndex,
        float* constants)
    {
        var effects = model.EquipmentEffects;
        if (effects is null || _transparentParticlePipeline is null || _denseParticlePipeline is null || _itemGlowPipeline is null)
            return;

        if (!_geometryCache.TryGetOrRequest(effects.Mesh, frameIndex, out var mesh))
            return;
        var vertexBufferView = mesh.VertexBufferViews[frameIndex];
        var indexBufferView = mesh.IndexBufferView;
        _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
        _commandList.IASetIndexBuffer(&indexBufferView);

        foreach (var surface in effects.Surfaces)
        {
            var texture = _textureCache.Get(surface.TextureName);
            if (texture is null || surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                continue;

            var shaderKind = ParticleShaderCatalog.ForMode(surface.TextureMode);
            _commandList.SetPipelineState(shaderKind switch
            {
                ParticleShaderKind.ItemGlow => _itemGlowPipeline,
                ParticleShaderKind.DenseItemParticle => _denseParticlePipeline,
                ParticleShaderKind.ItemParticle => _transparentParticlePipeline,
                _ => throw new InvalidOperationException(
                    $"Particle mode {surface.TextureMode} selected unsupported model shader {shaderKind}.")
            });

            _shaderConstants.WriteModelBase(constants, viewProjection, model.Transform, surface.Color);
            _shaderConstants.WriteTextureFlags(
                constants + ModelShaderLayout.TextureFlagsOffset,
                (float)surface.TextureMode,
                modelSceneDepth,
                surface.Phase,
                elapsedSeconds);
            SetRootConstantsIfChanged(
                ModelShaderLayout.ModelConstantsRootParameter,
                constants,
                ModelShaderLayout.ModelConstantsCount,
                0);
            _commandList.SetGraphicsRootDescriptorTable(
                ModelShaderLayout.ModelTextureRootParameter,
                SrvGpuHandle(texture.SrvSlot));
            _commandList.SetGraphicsRootDescriptorTable(
                ModelShaderLayout.ModelOverlayTextureRootParameter,
                SrvGpuHandle(_fallbackTextureSlot));
            var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
            _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
        }
    }

    private unsafe void RecordUntexturedMesh(ModelGpuMesh mesh, float* constants, float modelSceneDepth)
    {
        _shaderConstants.WriteTextureFlags(
            constants + ModelShaderLayout.TextureFlagsOffset,
            ModelShaderVariables.TextureModeNoTexture,
            ModelShaderVariables.TextureAnimationNone,
            modelSceneDepth,
            scaledAnimationTime: 0.0f);
        SetRootConstantsIfChanged(
            ModelShaderLayout.ModelConstantsRootParameter,
            constants + ModelShaderLayout.TextureFlagsOffset,
            ModelShaderLayout.TextureFlagsConstantsCount,
            ModelShaderLayout.TextureFlagsOffset);
        var fallback = SrvGpuHandle(_fallbackTextureSlot);
        _commandList.SetGraphicsRootDescriptorTable(ModelShaderLayout.ModelTextureRootParameter, fallback);
        _commandList.SetGraphicsRootDescriptorTable(ModelShaderLayout.ModelOverlayTextureRootParameter, fallback);
        _commandList.DrawIndexedInstanced((uint)mesh.IndexCount, 1, 0, 0, 0);
    }

    private unsafe void SetRootConstantsIfChanged(int parameter, float* constants, int count, int offset) =>
        _rootConstants.SetIfChanged(_commandList, parameter, constants, count, offset);

    private unsafe void WriteLighting(
        SacredCamera camera,
        SceneLighting lighting,
        Dx12DisplayProfile display,
        float* target)
    {
        _shaderConstants.WriteSceneConstants(
            target,
            lighting.LightPosition,
            lighting.SpecularIntensity,
            camera.EyePosition,
            lighting.Shininess,
            new Vector4(lighting.AmbientColor, lighting.AmbientIntensity),
            new Vector4(lighting.LightColor, lighting.DiffuseIntensity),
            new Vector4(
                display.ScenePaperWhiteNits,
                display.UiPaperWhiteNits,
                display.SunDiffuseNits,
                display.SunSpecularNits));
    }

    private static float CalculateSceneDepth(SacredCamera camera, SceneModel model)
    {
        // Keep painter ordering tied to the gameplay/collision anchor. Model-local geometry
        // (weapons, wings, effects) must not move the character between world depth layers.
        var depthKey = model.DepthAnchor.X + model.DepthAnchor.Y + model.DepthAnchor.Y * 0.001f;
        var centerDepthKey = camera.WorldCenter.X + camera.WorldCenter.Y + camera.WorldCenter.Y * 0.001f;
        var painterDepth = Math.Clamp(
            0.50f - (depthKey - centerDepthKey) * PainterDepthScale,
            0.20f,
            0.72f);
        return Math.Clamp(painterDepth + PlayerDepthBias, 0.0f, 1.0f);
    }

    private GpuDescriptorHandle SrvGpuHandle(int index) => _srvHeapStart + index * _descriptorSize;

}
