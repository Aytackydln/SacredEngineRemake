using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Sacred.UI.Inventory;
using Sacred.UI.Textures;

namespace AssetViewer.AssetViewer;

/// <summary>Host-owned bitmap crops; the original texture bytes and selectors stay in Sacred.UI.</summary>
internal sealed class EquipmentPanelArtwork(EquipmentPanelAssets assets) : IDisposable
{
    private readonly Dictionary<UiTextureRegion, WriteableBitmap> _bitmaps = [];

    public Bitmap Get(UiTextureRegion region)
    {
        if (_bitmaps.TryGetValue(region, out var cached)) return cached;
        var texture = assets.Textures[region.TextureName];
        var width = (int)region.Size.X;
        var height = (int)region.Size.Y;
        var x = (int)region.Authored.U0;
        var y = (int)region.Authored.V0;
        if (x < 0 || y < 0 || x + width > texture.Width || y + height > texture.Height)
            throw new ArgumentException($"UI crop exceeds {region.TextureName}.");
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var target = bitmap.Lock())
            for (var row = 0; row < height; row++)
                Marshal.Copy(texture.Rgba8, ((y + row) * texture.Width + x) * 4,
                    target.Address + row * target.RowBytes, width * 4);
        _bitmaps.Add(region, bitmap);
        return bitmap;
    }

    public void Dispose()
    {
        foreach (var bitmap in _bitmaps.Values) bitmap.Dispose();
        _bitmaps.Clear();
    }
}
