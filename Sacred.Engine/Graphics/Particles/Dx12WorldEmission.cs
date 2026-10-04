using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Scene.InGame;
using Sacred.Particles;
using Sacred.World.Particles;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Particles;

/// <summary>Opt-in persistent emitter ownership, projection and explicit restoration.</summary>
internal sealed class Dx12WorldEmission(ID3D12Device device, Dx12TextureUploader uploader, Action wait)
    : IWorldParticleEmissionBackend, IDisposable
{
    private readonly Dictionary<WorldGpuEmitter, WorldEmitterGpuInstance> _instances = [];
    private Dx12WorldEmissionKernels? _kernels;
    private string? _failure;
    private readonly Dictionary<WorldGpuEmitter, string> _blocked = [];
    public bool Enabled { get; set; }
    public bool DrawReady { get; set; }
    public bool IsAvailable => Enabled && DrawReady && _kernels is not null;
    public bool IsReady(WorldGpuEmitter emitter) => IsAvailable && !_blocked.ContainsKey(emitter);
    public bool HasState(WorldGpuEmitter emitter) => _instances.TryGetValue(emitter, out var instance) && instance.CommittedInitialized;
    public long SnapshotCount { get; private set; }
    public string Status => $"World GPU emission requested {Enabled}; ready {IsAvailable}; emitters {_instances.Count}; " +
        $"logical bytes {_instances.Values.Sum(i => (long)i.Bytes)}; committed bytes {_instances.Values.Sum(i => (long)i.CommittedBytes)}; " +
        $"snapshots {SnapshotCount}; paused emitters {_blocked.Count}; {_failure ?? "no CPU birth/lifetime mirror for selected world emitters"}";
    public void EnsureReady()
    {
        if (!Enabled || _kernels is not null || _failure is not null) return;
        try { _kernels = new(device); Console.WriteLine("GPU world emission pipelines ready."); }
        catch (Exception error) { device.DeviceRemovedReason.CheckError(); _failure = error.Message; Console.WriteLine($"GPU world emission unavailable: {_failure}"); }
    }
    public void ResetPipelines() { DrawReady = false; _failure = null; _kernels?.Dispose(); _kernels = null; }
    public void CommitSubmission() { foreach (var instance in _instances.Values) instance.Commit(); }
    public void Prepare(ID3D12GraphicsCommandList commands, IReadOnlyList<WorldGpuEmitter> emitters, Dx12FrameContext frame)
    {
        var active = emitters.ToHashSet();
        foreach (var key in _blocked.Keys.Where(e => !active.Contains(e)).ToArray()) _blocked.Remove(key);
        foreach (var key in _instances.Keys.Where(e => !active.Contains(e)).ToArray())
        { _instances[key].Retire(frame); _instances.Remove(key); }
        if (!IsAvailable) return;
        foreach (var emitter in emitters)
        {
            if (_blocked.ContainsKey(emitter)) continue;
            if (!_instances.TryGetValue(emitter, out var instance))
            {
                try { instance = new(device, uploader, emitter); _instances.Add(emitter, instance); }
                catch (Exception error)
                {
                    device.DeviceRemovedReason.CheckError(); _blocked.Add(emitter, error.Message);
                    Console.WriteLine($"GPU world emitter {emitter.Handle} paused: {error.Message}"); continue;
                }
            }
            instance.BeginRecording(); _kernels!.Record(commands, instance);
        }
    }
    public void Project(ID3D12GraphicsCommandList commands, Dx12ParticleKernels kernels,
        IReadOnlyList<WorldGpuEmitter> emitters, SacredCamera camera, int width, int height,
        ref int offset, Func<ParticleSpriteReference, (uint Texture, uint Encoding)> texture)
    {
        foreach (var emitter in emitters)
        {
            if (!_instances.TryGetValue(emitter, out var instance) || !instance.Initialized) continue;
            var binding = texture(emitter.Sprite);
            for (var s = 0; s < emitter.Parameters.Length; s++)
            {
                commands.SetComputeRootUnorderedAccessView(1, instance.States.GPUVirtualAddress + (ulong)instance.SourceData.ParticleOffset(s));
                commands.SetComputeRootShaderResourceView(4, instance.Colors[s].GPUVirtualAddress);
                var c = WorldEmitterGpuProjection.Constants(emitter, s, camera, width, height, offset, binding.Texture, binding.Encoding);
                kernels.Dispatch(commands, "project_particles", c, emitter.Definition.Capacity);
                offset += emitter.Definition.Capacity;
            }
            if (emitter.Definition.Halo is { } halo)
            {
                var sprite = new ParticleSpriteReference(halo.TextureName, 1, 1, 1, 1, ParticleShaderKind.ItemParticle);
                binding = texture(sprite);
                commands.SetComputeRootUnorderedAccessView(1, instance.States.GPUVirtualAddress +
                    (ulong)(WorldEmitterGpuSource.HeaderBytes + instance.SourceData.SetStride * instance.SourceData.SetCount));
                commands.SetComputeRootShaderResourceView(4, instance.HaloColors!.GPUVirtualAddress);
                var c = WorldEmitterGpuProjection.Constants(emitter, 0, camera, width, height, offset++, binding.Texture, binding.Encoding, true);
                kernels.Dispatch(commands, "project_particles", c, 1);
            }
        }
    }
    public unsafe WorldParticleEmissionSnapshot Snapshot(WorldGpuEmitter emitter)
    {
        wait(); device.DeviceRemovedReason.CheckError();
        if (_kernels is null && !HasState(emitter)) throw new InvalidOperationException("World emission snapshot has no readable live state.");
        if (!_instances.TryGetValue(emitter, out var instance))
        { instance = new(device, uploader, emitter); _instances.Add(emitter, instance); }
        WorldParticleEmissionSnapshot result;
        do { result = ReadSnapshot(instance); } while (emitter.PendingCount > 0 && _kernels is not null);
        return result;
    }
    private unsafe WorldParticleEmissionSnapshot ReadSnapshot(WorldEmitterGpuInstance instance)
    {
        var emitter = instance.SourceData.Emitter;
        using var queue = device.CreateCommandQueue(CommandListType.Direct);
        using var allocator = device.CreateCommandAllocator(CommandListType.Direct);
        using var commands = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, allocator, null);
        using var fence = device.CreateFence(0); using var signal = new AutoResetEvent(false);
        using var readback = ParticleGpuBuffers.Create(device, instance.SourceData.StateBytes, HeapType.Readback);
        instance.BeginRecording();
        if (_kernels is not null) _kernels.Record(commands, instance);
        Dx12TextureUploader.Transition(commands, instance.States, instance.State, ResourceStates.CopySource);
        commands.CopyBufferRegion(readback, 0, instance.States, 0, (ulong)instance.SourceData.StateBytes);
        Dx12TextureUploader.Transition(commands, instance.States, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
        instance.State = ResourceStates.UnorderedAccess;
        commands.Close(); queue.ExecuteCommandList(commands); queue.Signal(fence, 1).CheckError();
        fence.SetEventOnCompletion(1, signal.SafeWaitHandle.DangerousGetHandle()).CheckError();
        while (!signal.WaitOne(100)) device.DeviceRemovedReason.CheckError();
        device.DeviceRemovedReason.CheckError();
        if (fence.CompletedValue == ulong.MaxValue) throw new InvalidOperationException("Removed-device fence.");
        instance.Commit();
        void* mapped; readback.Map(0, null, &mapped).CheckError();
        try
        {
            SnapshotCount++; Console.WriteLine($"GPU world emission explicit snapshot: {emitter.Handle}.");
            return instance.SourceData.Decode(new ReadOnlySpan<byte>(mapped, instance.SourceData.StateBytes));
        }
        finally { readback.Unmap(0); }
    }
    public void Dispose() { foreach (var i in _instances.Values) i.Dispose(); _kernels?.Dispose(); }
}
