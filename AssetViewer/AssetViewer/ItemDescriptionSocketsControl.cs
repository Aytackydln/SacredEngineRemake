using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

/// <summary>Native socket cell colors and atlas overlay; populated item models are child controls.</summary>
internal sealed class ItemDescriptionSocketsControl : Decorator, IDisposable
{
    public Canvas Models { get; } = new();
    private readonly IReadOnlyList<ItemDescriptionSocket> _sockets;
    private readonly Dictionary<byte, WriteableBitmap> _overlays = new();

    public ItemDescriptionSocketsControl(ItemDescriptionAssets assets, IReadOnlyList<ItemDescriptionSocket> sockets)
    {
        _sockets = sockets;
        Child = Models;
        Width = sockets.Count * ItemDescriptionSocket.Pitch;
        Height = ItemDescriptionSocket.Pitch;
        var region = assets.Socket;
        var source = assets.Textures[region.TextureName];
        foreach (var socket in sockets)
        {
            if (_overlays.ContainsKey(socket.Kind)) continue;
            var width = (int)region.Size.X; var height = (int)region.Size.Y;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (((int)region.Authored.V0 + y) * source.Width + (int)region.Authored.U0 + x) * 4;
                var target = (y * width + x) * 4;
                pixels[target] = (byte)(source.Rgba8[at] * ((socket.Color >> 16) & 255) / 255);
                pixels[target + 1] = (byte)(source.Rgba8[at + 1] * ((socket.Color >> 8) & 255) / 255);
                pixels[target + 2] = (byte)(source.Rgba8[at + 2] * (socket.Color & 255) / 255);
                pixels[target + 3] = (byte)(source.Rgba8[at + 3] * 0x5F / 255);
            }
            var bitmap = new WriteableBitmap(new(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using (var buffer = bitmap.Lock())
                for (var row = 0; row < height; row++)
                    Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
            _overlays.Add(socket.Kind, bitmap);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach (var socket in _sockets)
        {
            var rectangle = new Rect(socket.Index * ItemDescriptionSocket.Pitch, 0, ItemDescriptionSocket.Size, ItemDescriptionSocket.Size);
            context.FillRectangle(new SolidColorBrush(Color.FromUInt32((socket.Color & 0xFFFFFF) | 0x8F000000)), rectangle);
            context.DrawImage(_overlays[socket.Kind], rectangle);
        }
    }

    public void Dispose() { foreach (var bitmap in _overlays.Values) bitmap.Dispose(); _overlays.Clear(); }
}
