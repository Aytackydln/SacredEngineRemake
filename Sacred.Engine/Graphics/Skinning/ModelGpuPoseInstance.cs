using System;
using Sacred.Engine.Graphics.Frames;
using Sacred.Granny.Animation;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>DEFAULT outputs per fence-retired frame slot, keyed by source, generation and sampled time.</summary>
internal sealed class ModelGpuPoseInstance : IDisposable
{
    private readonly ID3D12Resource?[] _buffers;
    private readonly ResourceStates[] _states;
    private readonly (GrnPoseInstanceHandle Handle, ulong Source, GrnAnimationClip Clip, float Time)?[] _versions;
    public ModelGpuPoseInstance(int frames)
    { _buffers = new ID3D12Resource[frames]; _states = new ResourceStates[frames]; _versions = new (GrnPoseInstanceHandle, ulong, GrnAnimationClip, float)?[frames]; }
    public ID3D12Resource? Buffer(int slot) => _buffers[slot];
    public int PaletteOffset { get; private set; }
    public int PaletteStride { get; private set; }
    public long DispatchCount { get; private set; }
    public bool Update(ID3D12Device device, ID3D12GraphicsCommandList commands, Dx12PoseKernels kernels,
        ModelGpuPoseSource source, in GrnPoseRequest request, Dx12FrameContext frame, Dx12GpuAnimationTimings? timings)
    {
        var slot = frame.Index; var data = source.Data;
        var version = Version(request);
        if (_versions[slot] is { } prior && prior == version) return false;
        if (_buffers[slot] is { } old && old.Description.Width != (ulong)data.OutputBytes)
        { frame.RetireResource(old); _buffers[slot] = null; }
        if (_buffers[slot] is null)
        {
            _buffers[slot] = device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                new ResourceDescription(ResourceDimension.Buffer, 0, (ulong)data.OutputBytes, 1, 1, 1, Format.Unknown,
                    1, 0, TextureLayout.RowMajor, ResourceFlags.AllowUnorderedAccess), ResourceStates.UnorderedAccess, null);
            _states[slot] = ResourceStates.UnorderedAccess;
        }
        var buffer = _buffers[slot]!;
        if (_states[slot] != ResourceStates.UnorderedAccess)
            Dx12TextureUploader.Transition(commands, buffer, _states[slot], ResourceStates.UnorderedAccess);
        kernels.Evaluate(commands, source.Buffer, buffer, data, request, timings);
        Dx12TextureUploader.Transition(commands, buffer, ResourceStates.UnorderedAccess, ResourceStates.NonPixelShaderResource);
        _states[slot] = ResourceStates.NonPixelShaderResource; _versions[slot] = version;
        PaletteOffset = data.PaletteOffset; PaletteStride = data.PaletteStride; DispatchCount++;
        return true;
    }
    public bool IsCurrent(int slot, in GrnPoseRequest request) => _versions[slot] == Version(request);
    public void InvalidateOutputs() => Array.Clear(_versions);
    private static (GrnPoseInstanceHandle, ulong, GrnAnimationClip, float) Version(in GrnPoseRequest request)
    {
        var time = request.TimeSeconds; var duration = request.Animation.DurationSeconds;
        if (request.TimeMode == GrnPoseTimeMode.Clamp) time = Math.Clamp(time, 0, MathF.Max(0, duration));
        else if (!float.IsFinite(time) || !float.IsFinite(duration) || duration <= .000001f) time = 0;
        else { time %= duration; if (time < 0) time += duration; }
        return (request.Instance, request.SourceVersion, request.Animation, time);
    }
    public void Retire(Dx12FrameContext frame)
    { for (var i = 0; i < _buffers.Length; i++) { if (_buffers[i] is { } b) frame.RetireResource(b); _buffers[i] = null; _versions[i] = null; } }
    public void Dispose() { foreach (var b in _buffers) b?.Dispose(); Array.Clear(_buffers); }
}
