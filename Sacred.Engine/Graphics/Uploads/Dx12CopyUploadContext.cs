using System;
using System.Collections.Generic;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Uploads;

internal sealed class Dx12CopyUploadContext : IDisposable
{
    public Dx12CopyUploadContext(ID3D12Device device)
    {
        Allocator = device.CreateCommandAllocator(CommandListType.Copy);
        Commands = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Copy, Allocator, null);
        Commands.Close();
    }
    public ID3D12CommandAllocator Allocator { get; }
    public ID3D12GraphicsCommandList Commands { get; }
    public List<ID3D12Resource> UploadBuffers { get; } = [];
    public HashSet<TextureUploadOperation> Operations { get; } = [];
    public ulong FenceValue { get; set; }
    public void Begin()
    {
        Allocator.Reset();
        Commands.Reset(Allocator, null);
    }
    public void Release()
    {
        foreach (var resource in UploadBuffers)
            resource.Dispose();
        UploadBuffers.Clear();
        Operations.Clear();
        FenceValue = 0;
    }
    public void Dispose()
    {
        Release();
        Commands.Dispose();
        Allocator.Dispose();
    }
}

internal sealed class TextureUploadOperation(TextureUploadTicket ticket)
{
    public TextureUploadTicket Ticket { get; } = ticket;
    public ID3D12Resource? Resource { get; set; }
    public int NextRow { get; set; }
    public ulong FenceValue { get; set; }
    public Exception? Error { get; set; }
}
