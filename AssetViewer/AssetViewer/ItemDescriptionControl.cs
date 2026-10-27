using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Sacred.Inventory.Items;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

/// <summary>Avalonia rendering adapter for the game's item frame and description lines.</summary>
internal sealed class ItemDescriptionControl : Decorator, IDisposable
{
    private readonly AssetViewerSession _session;
    private readonly ItemDescriptionFont _font;
    private readonly StackPanel _lines = new() { Spacing = 0 };
    private readonly StackPanel _headings = new();
    private readonly StackPanel _details = new();
    private readonly Decorator _socketHost = new();
    private readonly EquipmentSlotPreviewRenderer _previews;
    private readonly List<ItemDescriptionPreview> _socketModels = new();
    private readonly Dictionary<int, WriteableBitmap> _emblems = new();
    private readonly Dictionary<string, WriteableBitmap> _textures = new(StringComparer.OrdinalIgnoreCase);
    private ItemDescriptionAssets? _assets;
    private bool _disposed;
    private string _plainText = "";
    private SacredItemInstance? _item;
    private readonly Task _loading;
    public Task Ready => AwaitReadyAsync();

    public ItemDescriptionControl(AssetViewerSession session)
    {
        _session = session;
        _font = new(session.GameDirectory);
        _previews = new(session);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        MaxWidth = 560;
        MinWidth = 0;
        // Reserve the native corner overhangs as well as the tooltip's 4-pixel text inset.
        _lines.Margin = new Thickness(17, 15, 22, 14);
        _lines.Children.Add(_headings); _lines.Children.Add(_details); _lines.Children.Add(_socketHost);
        Child = _lines;
        _loading = LoadAsync();
    }

    public void Show(SacredItemInstance item)
    {
        _item = item;
        if (_assets is null) return;
        _headings.Children.Clear(); _details.Children.Clear();
        var content = new ItemDescriptionContentBuilder(_session.Data.GameResStore, _session.Data.ItemSets, _assets.EquipmentRules).Create(item);
        foreach (var line in content)
        {
            var text = LineText(line.Text, line.Color);
            Control row = text;
            if (line.IconSelector is { } selector)
            {
                var heading = new Grid { ColumnDefinitions = new("Auto,*") };
                heading.Children.Add(new Image { Source = _emblems[selector], Width = 16, Height = 16,
                    Margin = new Thickness(0, 2, 2, 0), VerticalAlignment = VerticalAlignment.Top });
                Grid.SetColumn(text, 1); heading.Children.Add(text); row = heading;
            }
            (line.IsHeading ? _headings : _details).Children.Add(row);
        }
        ShowSockets(item);
        _plainText = string.Join(Environment.NewLine, content.Select(line => line.Text));
        Console.WriteLine($"[Drops] Native item description: {content[0].Text}; {content.Count} lines.");
        InvalidateVisual();
    }

    public void Clear()
    {
        _headings.Children.Clear(); _details.Children.Clear(); ClearSockets();
        _plainText = ""; _item = null;
    }
    public void PrintDescription() => Console.WriteLine($"[Item description] {_plainText}");

    private TextBlock LineText(string text, uint color) => new()
    {
        Text = text, FontFamily = _font.Family, FontSize = 18, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromUInt32(color))
    };

    private async Task AwaitReadyAsync()
    {
        await _loading;
        await Task.WhenAll(_socketModels.Select(preview => preview.Ready));
    }

    private void ClearSockets()
    {
        foreach (var preview in _socketModels) preview.Dispose();
        _socketModels.Clear();
        if (_socketHost.Child is ItemDescriptionSocketsControl sockets) sockets.Dispose();
        _socketHost.Child = null;
    }

    private void ShowSockets(SacredItemInstance item)
    {
        ClearSockets();
        var sockets = ItemDescriptionSocket.Create(item);
        if (sockets.Count == 0) return;
        var row = new ItemDescriptionSocketsControl(_assets!, sockets)
        { Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var socket in sockets)
        {
            if (socket.ItemType == 0) continue;
            if (!_session.Data.GamePakStore.Weapons.TryGetValue(socket.ItemType, out var definition))
                throw new InvalidOperationException($"Socket model definition unavailable for resource {socket.ItemType}.");
            var preview = new ItemDescriptionPreview(_previews);
            preview.Show(definition);
            Canvas.SetLeft(preview, socket.Index * ItemDescriptionSocket.Pitch);
            row.Models.Children.Add(preview); _socketModels.Add(preview);
        }
        _socketHost.Child = row;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_assets is null || Bounds.Width <= 27 || Bounds.Height <= 21) return;
        var width = (float)Bounds.Width - 27;
        var height = (float)Bounds.Height - 21;
        var origin = new Vector(ItemDescriptionFrame.LeftExtent, ItemDescriptionFrame.TopExtent);
        context.FillRectangle(new SolidColorBrush(Color.FromUInt32(ItemDescriptionFrame.BackgroundArgb)),
            new Rect(origin.X, origin.Y, width, height));
        foreach (var (name, quad) in _assets.Frame.GetQuads(width, height))
        {
            var bitmap = _textures[name];
            var u0 = quad.Piece.UvMinimum.X * bitmap.PixelSize.Width;
            var v0 = quad.Piece.UvMinimum.Y * bitmap.PixelSize.Height;
            var u1 = quad.Piece.UvMaximum.X * bitmap.PixelSize.Width;
            var v1 = quad.Piece.UvMaximum.Y * bitmap.PixelSize.Height;
            var source = new Rect(Math.Min(u0, u1), Math.Min(v0, v1), Math.Abs(u1 - u0), Math.Abs(v1 - v0));
            var target = new Rect(quad.Position.X + origin.X, quad.Position.Y + origin.Y, quad.Size.X, quad.Size.Y);
            var flipX = u1 < u0; var flipY = v1 < v0;
            using (context.PushTransform(new Matrix(flipX ? -1 : 1, 0, 0, flipY ? -1 : 1,
                       flipX ? target.Left + target.Right : 0, flipY ? target.Top + target.Bottom : 0)))
                context.DrawImage(bitmap, source, target);
        }
    }

    private async Task LoadAsync()
    {
        var assets = await ItemDescriptionAssets.LoadAsync(_session.GameDirectory,
            name => _session.TextureCache.LoadTextureAsync(name));
        if (_disposed) return;
        foreach (var (name, texture) in assets.Textures)
        {
            var bitmap = new WriteableBitmap(new PixelSize(texture.Width, texture.Height), new Vector(96, 96),
                PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using (var target = bitmap.Lock())
                for (var row = 0; row < texture.Height; row++)
                    Marshal.Copy(texture.Rgba8, row * texture.Width * 4, target.Address + row * target.RowBytes, texture.Width * 4);
            _textures.Add(name, bitmap);
        }
        _assets = assets;
        foreach (var (selector, region) in assets.Emblems)
        {
            var texture = assets.Textures[region.TextureName];
            var width = (int)region.Size.X; var height = (int)region.Size.Y;
            var bitmap = new WriteableBitmap(new(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using (var target = bitmap.Lock())
                for (var row = 0; row < height; row++)
                    Marshal.Copy(texture.Rgba8, (((int)region.Authored.V0 + row) * texture.Width + (int)region.Authored.U0) * 4,
                        target.Address + row * target.RowBytes, width * 4);
            _emblems.Add(selector, bitmap);
        }
        if (_item is { } item) Show(item);
        InvalidateVisual();
        Console.WriteLine($"[Drops] Native item frame loaded: {assets.Textures.Count} original atlases.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearSockets(); _previews.Dispose();
        _font.Unregister();
        foreach (var bitmap in _emblems.Values) bitmap.Dispose();
        _emblems.Clear();
        foreach (var bitmap in _textures.Values) bitmap.Dispose();
        _textures.Clear(); _assets = null;
    }
}
