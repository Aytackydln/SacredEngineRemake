using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Uploads;

internal sealed class Dx12TextureCopyRecorder(ID3D12Device device)
{
    public ID3D12Resource CreateTexture(TextureUploadTicket ticket) => device.CreateCommittedResource(
        new HeapProperties(HeapType.Default), HeapFlags.None,
        new ResourceDescription(ResourceDimension.Texture2D, 0, (ulong)ticket.Width, (uint)ticket.Height,
            1, 1, Format.R8G8B8A8_UNorm, 1, 0, TextureLayout.Unknown, ResourceFlags.None),
        ResourceStates.Common, null);

    public static int RowPitch(int width) => checked((width * 4 + 255) & ~255);

    public unsafe int RecordRows(Dx12CopyUploadContext context, TextureUploadOperation operation, int availableBytes)
    {
        var ticket = operation.Ticket;
        var rowPitch = RowPitch(ticket.Width);
        var rows = Math.Min(ticket.Height - operation.NextRow, availableBytes / rowPitch);
        if (rows == 0)
            return 0;
        var size = checked(rows * rowPitch);
        var upload = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            new ResourceDescription(ResourceDimension.Buffer, 0, (ulong)size, 1, 1, 1, Format.Unknown,
                1, 0, TextureLayout.RowMajor, ResourceFlags.None), ResourceStates.GenericRead, null);
        context.UploadBuffers.Add(upload);
        void* mapped;
        upload.Map(0, null, &mapped).CheckError();
        try
        {
            var rowBytes = checked(ticket.Width * 4);
            for (var row = 0; row < rows; row++)
                Marshal.Copy(ticket.Pixels, checked((operation.NextRow + row) * rowBytes),
                    (nint)mapped + row * rowPitch, rowBytes);
        }
        finally { upload.Unmap(0, null); }
        var footprint = new PlacedSubresourceFootPrint
        {
            Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)ticket.Width, (uint)rows, 1, (uint)rowPitch)
        };
        // COMMON promotes to COPY_DEST on this queue, and decays after each submission.
        // Consumers transition COMMON -> PIXEL_SHADER_RESOURCE only after fence completion.
        context.Commands.CopyTextureRegion(new TextureCopyLocation(operation.Resource!, 0),
            0, (uint)operation.NextRow, 0, new TextureCopyLocation(upload, footprint), null);
        context.Operations.Add(operation);
        operation.NextRow += rows;
        return size;
    }
}
