using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Diagnostics;
using Sacred.Particles;
using Sacred.Shaders;
using Sacred.World.Particles;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Particles;

/// <summary>Persistent simulation, merged painter sorting and direct GPU rendering. GPU mode stays opt-in.</summary>
internal sealed class Dx12GpuParticlePass : IParticleGpuBackend, IDisposable
{
    private readonly ID3D12Device _device;
    private readonly Dx12TextureUploader _uploader;
    private readonly AssetManager _assets;
    private readonly Dx12SpritePass _sprites;
    private readonly Action _waitForGpu;
    private readonly Dictionary<ParticleGpuBatch,ParticleGpuBuffers> _buffers = new(ReferenceEqualityComparer.Instance);
    private Dx12ParticleKernels? _kernels;
    private Dx12CreatedPipelineGroup? _draw;
    private ID3D12Resource? _output;
    private int _capacity;
    private int _drawCount;
    private ulong _outputCommittedBytes;
    private ResourceStates _outputState = ResourceStates.UnorderedAccess;
    public Dx12GpuParticlePass(ID3D12Device device, Dx12TextureUploader uploader, AssetManager assets,
        Dx12SpritePass sprites, Action waitForGpu)
    { _device=device; _uploader=uploader; _assets=assets; _sprites=sprites; _waitForGpu=waitForGpu; }
    public bool IsAvailable => _kernels is not null && _draw is not null;
    public string Status => $"GPU particles available {IsAvailable}; batches {_buffers.Count}; " +
        $"persistent state/color bytes {_buffers.Values.Sum(b=>b.Bytes)}; draw buffer bytes {_output?.Description.Width ?? 0}; " +
        $"committed state/color bytes {_buffers.Values.Sum(b=>(long)b.CommittedBytes)}; committed draw bytes {_outputCommittedBytes}; " +
        $"sorted draw slots {_drawCount}; cumulative birth upload bytes {_kernels?.UploadedBytes ?? 0}; snapshots {SnapshotCount}";
    public int SnapshotCount { get; private set; }

    public void CreatePipelines(bool hdr, Format target, Format depth)
    {
        try
        {
            _kernels ??= new(_device,_uploader);
            var compiled = Dx12PipelineFactory.Compile(ParticleGpuShaderLayout.Draw(hdr),DxilShaderCompiler.Compile);
            var created = Dx12PipelineFactory.Create(_device,compiled,target,depth);
            DisposePipelines(); _draw=created;
            EngineLog.WriteLine("GPU particle pipeline gate passed: persistent compute, painter sort and direct drawing ready.");
        }
        catch (Exception error)
        { DisposePipelines(); EngineLog.WriteLine($"GPU particle fallback: pipeline creation failed: {error.Message}"); }
    }
    public void DisposePipelines()
    {
        if (_draw is null) return;
        foreach (var p in _draw.Pipelines.Values) p.Dispose(); _draw.RootSignature.Dispose(); _draw=null;
    }
    private ParticleGpuBuffers Get(ParticleGpuBatch batch)
    {
        if (!_buffers.TryGetValue(batch,out var buffers)) _buffers.Add(batch,buffers=new(_device,_uploader,batch));
        return buffers;
    }
    public ParticleSimulationState[] Snapshot(ParticleGpuBatch batch)
    {
        _waitForGpu();
        if (_kernels is null) throw new InvalidOperationException("GPU particle snapshot pipeline unavailable.");
        SnapshotCount++;
        EngineLog.WriteLine($"GPU particle explicit CPU snapshot: {batch.Count} live particles.");
        return ParticleGpuSnapshot.Read(_device,_kernels,batch,Get(batch));
    }
    private bool Texture(ParticleSpriteReference reference,out uint texture,out uint encoding)
    {
        texture=uint.MaxValue; encoding=2;
        if (!_assets.TryGetWorldParticleSpriteOrRequest(reference,out var sprite) || sprite is null ||
            !_sprites.TryGetParticleTexture(sprite,out texture)) { texture=uint.MaxValue; return false; }
        encoding=sprite.ChannelEncoding switch { SacredTextureChannelEncoding.AlphaMask=>0u,SacredTextureChannelEncoding.Argb=>1u,_=>2u };
        return true;
    }
    public void Prepare(ID3D12GraphicsCommandList commands, IReadOnlyList<WorldGpuParticleBatch> groups,
        IReadOnlyList<WorldParticle> cpuParticles, bool enabled, SacredCamera camera,
        Dx12FrameContext frame,int width,int height,Dx12GpuAnimationTimings timings)
    {
        var active=new HashSet<ParticleGpuBatch>(groups.Select(g=>g.Batch),ReferenceEqualityComparer.Instance);
        foreach (var batch in _buffers.Keys.Where(b=>!active.Contains(b)).ToArray())
        { _buffers[batch].Retire(frame); _buffers.Remove(batch); }
        _drawCount=0;
        if (!enabled)
        {
            if (_output is not null) frame.RetireResource(_output);
            _output=null; _capacity=0; _outputCommittedBytes=0;
            return;
        }
        if (!IsAvailable) return;
        using var cpuScope=AnimationPerformance.Measure(AnimationCpuStage.ParticleGpuPreparation);
        var count=checked(cpuParticles.Count+groups.Sum(g=>g.Batch.Capacity));
        if (count==0) return;
        var needed=1; while(needed<count) needed=checked(needed*2);
        if (_output is null || needed>_capacity)
        {
            if (_output is not null) frame.RetireResource(_output);
            _output=ParticleGpuBuffers.Create(_device,checked(needed*Marshal.SizeOf<ParticleGpuDraw>()));
            _outputCommittedBytes=_device.GetResourceAllocationInfo(0,_output.Description).SizeInBytes;
            _capacity=needed; _outputState=ResourceStates.UnorderedAccess;
        }
        if (_outputState!=ResourceStates.UnorderedAccess)
            Dx12TextureUploader.Transition(commands,_output,_outputState,ResourceStates.UnorderedAccess);
        commands.SetComputeRootSignature(_kernels!.Root);
        commands.SetComputeRootUnorderedAccessView(2,_output.GPUVirtualAddress);
        var c=new float[ParticleGpuShaderLayout.ComputeConstants];
        Dx12ParticleKernels.UInt(c,3,(uint)needed);
        _kernels.Dispatch(commands,"clear_draws",c,needed); Dx12ParticleKernels.Barrier(commands,_output);
        var inputs=new List<ParticleGpuDraw>(cpuParticles.Count);
        for(var i=0;i<cpuParticles.Count;i++)
            if(Texture(cpuParticles[i].Sprite,out var texture,out var encoding))
                inputs.Add(ParticleGpuProjection.Cpu(cpuParticles[i],camera,width,height,texture,encoding,(uint)i));
        if(inputs.Count>0)
        {
            var bytes=MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(inputs));
            var upload=_uploader.CreateUploadBuffer(bytes); frame.RetireResource(upload);
            AnimationPerformance.RecordParticleUpload(bytes.Length);
            commands.SetComputeRootShaderResourceView(5,upload.GPUVirtualAddress);
            Dx12ParticleKernels.UInt(c,37,(uint)inputs.Count);
            _kernels.Dispatch(commands,"copy_cpu_draws",c,inputs.Count); Dx12ParticleKernels.Barrier(commands,_output);
        }
        var offset=inputs.Count;
        var before=_kernels.UploadedBytes;
        // One fence-retained birth upload for the frame, rather than one committed resource per emission.
        var operations=groups.Select(g=>g.Batch.TakePending()).ToArray();
        var births=new List<ParticleGpuSpawn>();
        foreach(var plan in operations)
        foreach(var operation in plan)
        foreach(var birth in operation.Births) births.Add(new(birth.Slot,birth.State));
        ID3D12Resource? birthUpload=null;
        if(births.Count>0)
        {
            birthUpload=_uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(births)));
            frame.RetireResource(birthUpload);
        }
        var birthOffset=0;
        ulong BirthAddress(ParticleGpuSpawn[] packet)
        {
            var address=birthUpload!.GPUVirtualAddress+(ulong)(birthOffset*Marshal.SizeOf<ParticleGpuSpawn>());
            birthOffset+=packet.Length;
            return address;
        }
        using(timings.Measure(AnimationGpuStage.ParticleSimulation))
            for(var i=0;i<groups.Count;i++)
                _kernels.RecordPreparedUpdates(commands,groups[i].Batch,Get(groups[i].Batch),operations[i],BirthAddress);
        AnimationPerformance.RecordParticleUpload(checked((int)(_kernels.UploadedBytes-before)));
        using(timings.Measure(AnimationGpuStage.ParticleSort))
        {
            foreach(var group in groups)
            {
                Texture(group.Sprite,out var texture,out var encoding);
                var buffer=Get(group.Batch);
                commands.SetComputeRootUnorderedAccessView(1,buffer.States.GPUVirtualAddress);
                commands.SetComputeRootShaderResourceView(4,buffer.Colors.GPUVirtualAddress);
                var constants=ParticleGpuProjection.Constants(group,camera,width,height,offset,texture,encoding);
                _kernels.Dispatch(commands,"project_particles",constants,group.Batch.Capacity);
                offset+=group.Batch.Capacity;
            }
            Dx12ParticleKernels.Barrier(commands,_output);
            for(var k=2;k<=needed;k*=2)
            for(var j=k/2;j>0;j/=2)
            {
                Dx12ParticleKernels.UInt(c,15,(uint)j); Dx12ParticleKernels.UInt(c,16,(uint)k);
                _kernels.Dispatch(commands,"sort_particles",c,needed); Dx12ParticleKernels.Barrier(commands,_output);
            }
        }
        Dx12TextureUploader.Transition(commands,_output,ResourceStates.UnorderedAccess,ResourceStates.NonPixelShaderResource);
        _outputState=ResourceStates.NonPixelShaderResource; _drawCount=needed;
    }
    public unsafe void Draw(ID3D12GraphicsCommandList commands,Dx12DisplayProfile profile,int width,int height,Dx12GpuAnimationTimings timings)
    {
        if (_drawCount==0 || _draw is null || _output is null) return;
        using var scope=timings.Measure(AnimationGpuStage.ParticleDraws);
        var c=stackalloc float[StaticSpriteShaderLayout.SceneConstantsCount];
        new StaticSpriteShaderConstantsUpdater().Write(c,new(new(width,height),64/255f,Vector3.One,
            profile.ScenePaperWhiteNits,profile.UnlitSpriteNits,0,1,0));
        c[11]=0;
        commands.SetGraphicsRootSignature(_draw.RootSignature);
        commands.SetPipelineState(_draw[Dx12PipelineKind.PostModelTransparentUnlitStaticSprite]);
        commands.SetGraphicsRoot32BitConstants(0,StaticSpriteShaderLayout.SceneConstantsCount,c,0);
        commands.SetGraphicsRootShaderResourceView(1,_output.GPUVirtualAddress);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.DrawInstanced(6,(uint)_drawCount,0,0);
    }
    public void Dispose()
    { DisposePipelines(); _kernels?.Dispose(); _output?.Dispose(); foreach(var b in _buffers.Values)b.Dispose(); _buffers.Clear(); }
}
