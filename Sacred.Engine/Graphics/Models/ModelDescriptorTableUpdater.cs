using System;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Avoids redundant descriptor-table writes while a model pass retains one root signature.</summary>
internal sealed class ModelDescriptorTableUpdater
{
    private readonly GpuDescriptorHandle[] _handles;
    private readonly bool[] _known;

    public ModelDescriptorTableUpdater(int rootParameterCount)
    {
        _handles = new GpuDescriptorHandle[rootParameterCount];
        _known = new bool[rootParameterCount];
    }

    public void Reset() => Array.Clear(_known);

    public void SetIfChanged(
        ID3D12GraphicsCommandList commandList,
        int rootParameterIndex,
        GpuDescriptorHandle handle)
    {
        if (_known[rootParameterIndex] && _handles[rootParameterIndex] == handle)
            return;

        commandList.SetGraphicsRootDescriptorTable((uint)rootParameterIndex, handle);
        _handles[rootParameterIndex] = handle;
        _known[rootParameterIndex] = true;
    }
}
