using System;
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
        var page = context.Staging ??= new Dx12UploadPage(device);
        var rows = Math.Min(ticket.Height - operation.NextRow, Math.Min(availableBytes, page.AvailableBytes) / rowPitch);
        if (rows == 0)
            return 0;
        var size = checked(rows * rowPitch);
        var mapped = page.Allocate(size, out var offset);
        var rowBytes = checked(ticket.Width * 4);
        fixed (byte* pixels = ticket.Pixels)
        {
            var source = pixels + checked(operation.NextRow * rowBytes);
            if (rowBytes == rowPitch)
                Buffer.MemoryCopy(source, (void*)mapped, size, size);
            else
                for (var row = 0; row < rows; row++)
                    Buffer.MemoryCopy(source + row * rowBytes, (byte*)mapped + row * rowPitch, rowBytes, rowBytes);
        }
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = offset,
            Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)ticket.Width, (uint)rows, 1, (uint)rowPitch)
        };
        // COMMON promotes to COPY_DEST on this queue, and decays after each submission.
        // Consumers transition COMMON -> PIXEL_SHADER_RESOURCE only after fence completion.
        context.Commands.CopyTextureRegion(new TextureCopyLocation(operation.Resource!, 0),
            0, (uint)operation.NextRow, 0, new TextureCopyLocation(page.Resource, footprint), null);
        context.Operations.Add(operation);
        operation.NextRow += rows;
        return size;
    }
}
