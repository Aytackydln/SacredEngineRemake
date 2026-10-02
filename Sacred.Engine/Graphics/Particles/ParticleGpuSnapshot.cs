using System;
using System.Collections.Generic;
using System.Threading;
using Sacred.Particles;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Particles;

/// <summary>Explicit backend-transition/tool readback; never called by ordinary GPU playback.</summary>
internal static class ParticleGpuSnapshot
{
    public static unsafe ParticleSimulationState[] Read(ID3D12Device device, Dx12ParticleKernels kernels,
        ParticleGpuBatch batch, ParticleGpuBuffers buffers)
    {
        using var queue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        using var allocator = device.CreateCommandAllocator(CommandListType.Direct);
        using var commands = device.CreateCommandList<ID3D12GraphicsCommandList>(0,CommandListType.Direct,allocator);
        using var fence = device.CreateFence(0);
        using var signal = new AutoResetEvent(false);
        using var readback = ParticleGpuBuffers.Create(device,checked((int)buffers.States.Description.Width),HeapType.Readback);
        var temporary = new List<ID3D12Resource>();
        try
        {
            kernels.RecordUpdates(commands,batch,buffers,temporary.Add);
            Dx12TextureUploader.Transition(commands,buffers.States,ResourceStates.UnorderedAccess,ResourceStates.CopySource);
            commands.CopyBufferRegion(readback,0,buffers.States,0,buffers.States.Description.Width);
            Dx12TextureUploader.Transition(commands,buffers.States,ResourceStates.CopySource,ResourceStates.UnorderedAccess);
            commands.Close(); queue.ExecuteCommandList(commands); queue.Signal(fence,1).CheckError();
            fence.SetEventOnCompletion(1,signal.SafeWaitHandle.DangerousGetHandle()).CheckError(); signal.WaitOne();
            void* mapped; readback.Map(0,null,&mapped).CheckError();
            try
            {
                var states = new ReadOnlySpan<ParticleSimulationState>(mapped,batch.Capacity);
                var result = new ParticleSimulationState[batch.Count];
                for (var i = 0; i < result.Length; i++) result[i] = states[batch.LiveSlots[i]];
                return result;
            }
            finally { readback.Unmap(0); }
        }
        finally { foreach (var resource in temporary) resource.Dispose(); }
    }
}
