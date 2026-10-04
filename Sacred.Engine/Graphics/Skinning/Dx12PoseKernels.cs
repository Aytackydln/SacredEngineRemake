using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using Sacred.Engine.Graphics.Frames;
using Sacred.Granny.Animation;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Ordered pose dispatches on the existing graphics queue. No pose result is read on CPU.</summary>
internal sealed class Dx12PoseKernels : IDisposable
{
    private readonly Dictionary<string, ID3D12PipelineState> _pipelines = [];
    public ID3D12RootSignature Root { get; }
    public Dx12PoseKernels(ID3D12Device device)
    {
        if (!device.CheckFeatureSupport<FeatureDataD3D12Options>(Feature.Options).DoublePrecisionFloatShaderOps)
            throw new NotSupportedException("GPU pose reference normalization requires double precision shader operations.");
        var description = PoseGpuShaderLayout.Root();
        Root = device.CreateRootSignature(in description, RootSignatureVersion.Version1);
        try
        {
            foreach (var entry in new[] { "sample_locals", "evaluate_world", "create_palettes" })
                _pipelines.Add(entry, device.CreateComputePipelineState(new ComputePipelineStateDescription
                { RootSignature = Root, ComputeShader = DxilShaderCompiler.Compile(PoseGpuShaderLayout.Shader(entry)) }));
        }
        catch { Dispose(); throw; }
    }
    public unsafe void Evaluate(ID3D12GraphicsCommandList commands, ID3D12Resource source,
        ID3D12Resource output, GpuPoseSourceData data, in GrnPoseRequest request,
        Dx12GpuAnimationTimings? timings = null)
    {
        if (request.TimeMode == GrnPoseTimeMode.Clamp &&
            (!float.IsFinite(request.TimeSeconds) || !float.IsFinite(data.Duration)))
            throw new ArgumentOutOfRangeException(nameof(request), "GPU clamped poses require finite time and duration.");
        uint* constants = stackalloc uint[PoseGpuShaderLayout.ConstantCount];
        constants[0] = BitConverter.SingleToUInt32Bits(request.TimeSeconds);
        constants[1] = BitConverter.SingleToUInt32Bits(data.Duration);
        constants[2] = (uint)request.TimeMode; constants[3] = (uint)data.AnimationCount;
        constants[4] = (uint)data.BoneCount; constants[5] = 0; constants[6] = (uint)data.SkinOffset;
        constants[7] = Fma.IsSupported || AdvSimd.IsSupported ? 1u : 0u;
        commands.SetComputeRootSignature(Root);
        commands.SetComputeRootShaderResourceView(1, source.GPUVirtualAddress);
        commands.SetComputeRootUnorderedAccessView(2, output.GPUVirtualAddress);
        commands.SetComputeRoot32BitConstants(0, PoseGpuShaderLayout.ConstantCount, constants, 0);
        using (timings?.Measure(AnimationGpuStage.ClipSampling) ?? default)
        {
            if (data.AnimationCount > 0) Dispatch(commands, "sample_locals", data.AnimationCount);
            Barrier(commands, output);
        }
        using (timings?.Measure(AnimationGpuStage.Hierarchy) ?? default)
        {
            for (var level = 0; level <= data.MaximumLevel && data.BoneCount > 0; level++)
            {
                commands.SetComputeRoot32BitConstant(0, (uint)level, 5);
                Dispatch(commands, "evaluate_world", data.BoneCount); Barrier(commands, output);
            }
            Dispatch(commands, "create_palettes", Math.Max(1, data.BoneCount)); Barrier(commands, output);
        }
    }
    private void Dispatch(ID3D12GraphicsCommandList commands, string entry, int count)
    { commands.SetPipelineState(_pipelines[entry]); commands.Dispatch((uint)((count + 63) / 64), 1, 1); }
    private static void Barrier(ID3D12GraphicsCommandList commands, ID3D12Resource output) =>
        commands.ResourceBarrier(new ResourceBarrier(new ResourceUnorderedAccessViewBarrier(output)));
    public void Dispose() { foreach (var p in _pipelines.Values) p.Dispose(); Root.Dispose(); }
}
