using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Particles;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Particles;

/// <summary>Ordered graphics-queue compute; every dependent UAV operation has an explicit barrier.</summary>
internal sealed class Dx12ParticleKernels : IDisposable
{
    private readonly Dictionary<string, ID3D12PipelineState> _pipelines = [];
    private readonly Dx12TextureUploader _uploader;
    public ID3D12RootSignature Root { get; }
    public long UploadedBytes { get; private set; }
    public Dx12ParticleKernels(ID3D12Device device, Dx12TextureUploader uploader)
    {
        _uploader = uploader;
        var root = ParticleGpuShaderLayout.ComputeRoot();
        Root = device.CreateRootSignature(in root,RootSignatureVersion.Version1);
        try
        {
            foreach (var entry in new[] { "clear_states", "spawn_particles", "advance_particles", "clear_draws", "copy_cpu_draws", "project_particles", "sort_particles" })
                _pipelines.Add(entry,device.CreateComputePipelineState(new ComputePipelineStateDescription
                { RootSignature = Root, ComputeShader = DxilShaderCompiler.Compile(ParticleGpuShaderLayout.Shader(entry,"cs_6_6")) }));
        }
        catch { Dispose(); throw; }
    }
    public static void Barrier(ID3D12GraphicsCommandList commands, ID3D12Resource resource) => commands.ResourceBarrier(new ResourceBarrier(new ResourceUnorderedAccessViewBarrier(resource)));
    public unsafe void Dispatch(ID3D12GraphicsCommandList commands, string entry, float[] constants, int count)
    {
        commands.SetComputeRootSignature(Root);
        commands.SetPipelineState(_pipelines[entry]);
        fixed (float* data = constants) commands.SetComputeRoot32BitConstants(0,ParticleGpuShaderLayout.ComputeConstants,data,0);
        commands.Dispatch((uint)((count+63)/64),1,1);
    }
    public void RecordUpdates(ID3D12GraphicsCommandList commands, ParticleGpuBatch batch,
        ParticleGpuBuffers buffers, Action<ID3D12Resource> retain)
    {
        RecordPreparedUpdates(commands,batch,buffers,batch.TakePending(),births =>
        {
            var upload = _uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(births.AsSpan()));
            retain(upload);
            return upload.GPUVirtualAddress;
        });
    }
    public void RecordPreparedUpdates(ID3D12GraphicsCommandList commands, ParticleGpuBatch batch,
        ParticleGpuBuffers buffers, IReadOnlyList<ParticleGpuOperation> operations,
        Func<ParticleGpuSpawn[],ulong> birthAddress)
    {
        commands.SetComputeRootSignature(Root);
        commands.SetComputeRootUnorderedAccessView(1,buffers.States.GPUVirtualAddress);
        var constants = Constants(batch);
        if (!buffers.Initialized)
        {
            Dispatch(commands,"clear_states",constants,batch.Capacity);
            Barrier(commands,buffers.States); buffers.Initialized = true;
        }
        foreach (var operation in operations)
        {
            if (operation.Births.Length > 0)
            {
                var births = new ParticleGpuSpawn[operation.Births.Length];
                for (var i = 0; i < births.Length; i++) births[i] = new(operation.Births[i].Slot,operation.Births[i].State);
                UploadedBytes += births.Length * Marshal.SizeOf<ParticleGpuSpawn>();
                commands.SetComputeRootShaderResourceView(3,birthAddress(births));
                UInt(constants,2,(uint)births.Length);
                Dispatch(commands,"spawn_particles",constants,births.Length);
            }
            else if (operation.Step is { } step)
            {
                var motion = operation.Motion ?? batch.Parameters.Motion;
                constants[5] = motion.FadeChangeRate; constants[6] = motion.SizeChangeRate;
                constants[7] = motion.GravityChangeRate; constants[11] = motion.InwardAcceleration;
                constants[12] = motion.AdditionalAngularVelocity;
                constants[4] = step;
                constants[8] = operation.Direction.X; constants[9] = operation.Direction.Y; constants[10] = operation.Direction.Z;
                constants[13] = operation.GroundHeight; UInt(constants,14,(uint)operation.Collision);
                Dispatch(commands,"advance_particles",constants,batch.Capacity);
            }
            Barrier(commands,buffers.States);
        }
    }
    public static float[] Constants(ParticleGpuBatch batch)
    {
        var motion = batch.Parameters.Motion;
        var c = new float[ParticleGpuShaderLayout.ComputeConstants];
        c[ParticleGpuShaderLayout.HalfSizeMultiplierOffset] = 1;
        UInt(c,0,(uint)batch.Capacity);
        c[5] = motion.FadeChangeRate; c[6] = motion.SizeChangeRate; c[7] = motion.GravityChangeRate;
        c[11] = motion.InwardAcceleration; c[12] = motion.AdditionalAngularVelocity;
        return c;
    }
    public static void UInt(float[] values, int index, uint value) => values[index] = BitConverter.UInt32BitsToSingle(value);
    public void Dispose() { foreach (var p in _pipelines.Values) p.Dispose(); Root.Dispose(); }
}
