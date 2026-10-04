using System;
using Sacred.Granny.Diagnostics;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Frames;

internal enum AnimationGpuStage { ModelDraws, ModelShadows, Skinning, BoundsReduction, SkinnedModelDraws, SkinnedShadowDraws, ParticleSimulation, ParticleSort, ParticleDraws, ClipSampling, Hierarchy, PoseQueries, ParticleEmission, EquipmentSimulation, ParticleCompaction }

/// <summary>Timestamp pairs read only after the existing frame fence retires; no profiling waits.</summary>
internal sealed class Dx12GpuAnimationTimings : IDisposable
{
    private const int MaximumScopes = 1024;
    private const int QueriesPerFrame = MaximumScopes * 2;
    private readonly ID3D12GraphicsCommandList _commands;
    private readonly ID3D12QueryHeap _heap;
    private readonly ID3D12Resource _readback;
    private readonly AnimationGpuStage[][] _stages;
    private readonly int[] _counts;
    private readonly bool[] _resolved;
    private readonly double[] _milliseconds = new double[Enum.GetValues<AnimationGpuStage>().Length];
    private readonly long[] _samples = new long[Enum.GetValues<AnimationGpuStage>().Length];
    private readonly double _millisecondsPerTick;
    private int _frameIndex;
    private bool _recording;
    private int _completedFrames;
    private long _omittedScopes;

    public Dx12GpuAnimationTimings(ID3D12Device device, ID3D12CommandQueue queue,
        ID3D12GraphicsCommandList commands, int frameCount)
    {
        _commands = commands;
        queue.GetTimestampFrequency(out var frequency).CheckError();
        if (frequency == 0) throw new InvalidOperationException("GPU timestamp frequency is zero.");
        _millisecondsPerTick = 1000.0 / frequency;
        _counts = new int[frameCount];
        _resolved = new bool[frameCount];
        _stages = new AnimationGpuStage[frameCount][];
        for (var i = 0; i < frameCount; i++) _stages[i] = new AnimationGpuStage[MaximumScopes];
        _heap = device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, (uint)(QueriesPerFrame * frameCount)));
        try
        {
            _readback = device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
                new ResourceDescription(ResourceDimension.Buffer, 0, (ulong)(QueriesPerFrame * frameCount * sizeof(ulong)),
                    1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None), ResourceStates.CopyDest, null);
        }
        catch { _heap.Dispose(); throw; }
    }

    public void BeginFrame(int frameIndex)
    {
        _frameIndex = frameIndex;
        _counts[frameIndex] = 0;
        _recording = AnimationPerformance.Enabled;
    }

    public Scope Measure(AnimationGpuStage stage)
    {
        if (!_recording) return default;
        var count = _counts[_frameIndex];
        if (count == MaximumScopes) { _omittedScopes++; return default; }
        _stages[_frameIndex][count] = stage;
        _counts[_frameIndex]++;
        var query = (uint)(_frameIndex * QueriesPerFrame + count * 2);
        _commands.EndQuery(_heap, QueryType.Timestamp, query);
        return new Scope(this, query);
    }

    public void ResolveFrame()
    {
        var count = _counts[_frameIndex];
        if (!_recording || count == 0) { _recording = false; return; }
        var offset = (uint)(_frameIndex * QueriesPerFrame);
        _commands.ResolveQueryData(_heap, QueryType.Timestamp, offset, (uint)(count * 2), _readback, offset * sizeof(ulong));
        _resolved[_frameIndex] = true;
        _recording = false;
    }

    /// <summary>The caller has already completed this slot's submission fence.</summary>
    public unsafe void ReadCompletedFrame(int frameIndex)
    {
        if (!_resolved[frameIndex]) return;
        var offset = frameIndex * QueriesPerFrame;
        var count = _counts[frameIndex];
        void* mapped;
        _readback.Map(0, null, &mapped).CheckError();
        try
        {
            var values = (ulong*)mapped + offset;
            for (var i = 0; i < count; i++)
            {
                var start = values[i * 2];
                var end = values[i * 2 + 1];
                if (end < start) continue;
                var stage = (int)_stages[frameIndex][i];
                _milliseconds[stage] += (end - start) * _millisecondsPerTick;
                _samples[stage]++;
            }
        }
        finally { _readback.Unmap(0, null); }
        _completedFrames++;
        _resolved[frameIndex] = false;
    }

    public GpuAnimationTimingSnapshot CaptureAndReset()
    {
        var result = new GpuAnimationTimingSnapshot((double[])_milliseconds.Clone(), (long[])_samples.Clone(), _completedFrames, _omittedScopes);
        Array.Clear(_milliseconds);
        Array.Clear(_samples);
        _completedFrames = 0;
        _omittedScopes = 0;
        return result;
    }

    public void Dispose() { _readback.Dispose(); _heap.Dispose(); }

    public readonly struct Scope(Dx12GpuAnimationTimings owner, uint query) : IDisposable
    {
        public void Dispose() { if (owner is not null) owner._commands.EndQuery(owner._heap, QueryType.Timestamp, query + 1); }
    }
}

internal sealed record GpuAnimationTimingSnapshot(double[] Milliseconds, long[] Samples, int CompletedFrames, long OmittedScopes);
