using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.CombatArts;
using Sacred.UI.CombatArts;

namespace AssetViewer.AssetViewer;

/// <summary>Owns archive-backed native combat-art icons for one Character window.</summary>
internal sealed class CharacterCombatArtPresentation : IDisposable
{
    private readonly AssetViewerSession _session;
    private readonly bool _gold;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Dictionary<ushort, Bitmap> _icons = new();
    private readonly Dictionary<ushort, List<Image>> _pending = new();
    private bool _disposed;

    public CharacterCombatArtPresentation(AssetViewerSession session)
    {
        _session = session;
        _gold = session.Data.Creatures?.Entries.FirstOrDefault()?.RecordSize == 86;
        Ready = LoadAsync();
    }

    public Task Ready { get; }

    public Control Create(ushort code, string name)
    {
        var image = new Image { Width = 36, Height = 36, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        if (_icons.TryGetValue(code, out var icon)) image.Source = icon;
        else if (!_disposed)
        {
            if (!_pending.TryGetValue(code, out var images)) _pending[code] = images = [];
            images.Add(image);
        }
        ToolTip.SetTip(image, name);
        var grid = new Grid { ColumnDefinitions = new("40,*"), ColumnSpacing = 4 };
        grid.Children.Add(image);
        var text = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        return grid;
    }

    public void Print(ushort code, string name)
    {
        var selector = CharacterCombatArtIconSelectors.Resolve(code, _gold);
        Console.WriteLine($"[Combat art] {code}: {name}; icon {selector?.TextureName ?? "unmapped"}; cell {selector?.X},{selector?.Y}; loaded {_icons.ContainsKey(code)}.");
    }

    private async Task LoadAsync()
    {
        var loaded = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var codes = _session.Data.CombatArts.Entries.Select(art => art.Code)
                .Concat(_session.Data.Creatures?.Entries.SelectMany(SacredCombatArtStartingAllocation.ForCreature) ?? [])
                .Distinct().ToArray();
            foreach (var code in codes)
            {
                _cancel.Token.ThrowIfCancellationRequested();
                if (CharacterCombatArtIconSelectors.Resolve(code, _gold) is not { } selector) continue;
                try
                {
                    if (!loaded.TryGetValue(selector.TextureName, out var texture))
                        loaded[selector.TextureName] = texture = await _session.Textures.LoadTextureAsync(selector.TextureName, _cancel.Token);
                    _cancel.Token.ThrowIfCancellationRequested();
                    var bitmap = Crop(texture, selector);
                    _icons.Add(code, bitmap);
                    if (_pending.Remove(code, out var images))
                        foreach (var image in images) image.Source = bitmap;
                }
                catch (Exception error) when (error is IOException or ArgumentException)
                {
                    Console.WriteLine($"[Assets] Combat-art icon unavailable: {code}; {selector.TextureName}; {error.Message}");
                }
            }
            Console.WriteLine($"[Assets] Combat-art icons loaded: {_icons.Count}/{codes.Length}; {loaded.Count} native atlases; authored colors preserved.");
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
    }

    private static WriteableBitmap Crop(TextureAsset texture, CharacterCombatArtIcon selector)
    {
        const int size = 64;
        if (selector.X < 0 || selector.Y < 0 || selector.X + size > texture.Width || selector.Y + size > texture.Height)
            throw new ArgumentException("Native icon rectangle exceeds its authored texture.");
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var target = bitmap.Lock();
        var pixels = texture.Rgba8;
        for (var row = 0; row < size; row++)
            Marshal.Copy(pixels, ((selector.Y + row) * texture.Width + selector.X) * 4,
                target.Address + row * target.RowBytes, size * 4);
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _cancel.Cancel();
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear(); _pending.Clear();
        // A pending archive load may still observe the cancellation token.
        _ = Ready.ContinueWith(_ => _cancel.Dispose(), TaskScheduler.Default);
    }
}
