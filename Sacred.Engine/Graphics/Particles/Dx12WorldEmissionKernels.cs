using System;
using System.Collections.Generic;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Particles;

internal sealed class Dx12WorldEmissionKernels : IDisposable
{
    private readonly Dictionary<string, ID3D12PipelineState> _pipelines = [];
    public Dx12WorldEmissionKernels(ID3D12Device device)
    {
        if (!device.CheckFeatureSupport<FeatureDataD3D12Options>(Feature.Options).DoublePrecisionFloatShaderOps)
            throw new NotSupportedException("World emission compatibility RNG requires shader double precision.");
        var root = WorldEmitterGpuShaderLayout.Root();
        Root = device.CreateRootSignature(in root, RootSignatureVersion.Version1);
        try
        {
            foreach (var entry in new[] { "initialize", "emit_before", "movement", "release_and_emit" })
                _pipelines.Add(entry, device.CreateComputePipelineState(new ComputePipelineStateDescription {
                    RootSignature = Root, ComputeShader = DxilShaderCompiler.Compile(WorldEmitterGpuShaderLayout.Shader(entry)) }));
        }
        catch { Dispose(); throw; }
    }
    public ID3D12RootSignature Root { get; }
    public unsafe void Record(ID3D12GraphicsCommandList commands, WorldEmitterGpuInstance instance)
    {
        var data = instance.SourceData; var emitter = data.Emitter;
        if (instance.State != ResourceStates.UnorderedAccess)
            Dx12TextureUploader.Transition(commands, instance.States, instance.State, ResourceStates.UnorderedAccess);
        instance.State = ResourceStates.UnorderedAccess;
        commands.SetComputeRootSignature(Root);
        commands.SetComputeRootShaderResourceView(1, instance.Source.GPUVirtualAddress);
        commands.SetComputeRootUnorderedAccessView(2, instance.States.GPUVirtualAddress);
        var controls = new uint[] { 0, BitConverter.SingleToUInt32Bits(-emitter.HeightOffset),
            (uint)data.Capacity, (uint)data.SetCount, (uint)data.SetStride, (uint)data.StateBytes, (uint)data.InitialOffset, 0 };
        void Dispatch(string entry, int threads, int lanes)
        {
            commands.SetPipelineState(_pipelines[entry]);
            fixed (uint* p = controls) commands.SetComputeRoot32BitConstants(0, (uint)controls.Length, p, 0);
            commands.Dispatch((uint)((threads + lanes - 1) / lanes), 1, 1);
            Dx12ParticleKernels.Barrier(commands, instance.States);
        }
        if (!instance.Initialized) { Dispatch("initialize", data.StateBytes / 4, 64); instance.Initialized = true; }
        var rounds = 0;
        foreach (var step in emitter.Pending)
        {
            if (step.Sequence <= instance.AppliedSequence) continue;
            if (rounds++ == 32) break; // Retain excess steps; never discard or merge them.
            controls[0] = BitConverter.SingleToUInt32Bits(step.Seconds);
            Dispatch("emit_before", 1, 1);
            Dispatch("movement", checked(data.Capacity * data.SetCount), 64);
            Dispatch("release_and_emit", 1, 1);
            instance.AppliedSequence = step.Sequence;
        }
    }
    public void Dispose() { foreach (var p in _pipelines.Values) p.Dispose(); Root.Dispose(); }
}
