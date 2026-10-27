using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Sacred.Core.Pak.Weapon;

namespace AssetViewer.AssetViewer;

/// <summary>A populated socket's item model on a transparent surface.</summary>
internal sealed class ItemDescriptionPreview(EquipmentSlotPreviewRenderer renderer) : Canvas, IDisposable
{
    private readonly Image _image = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
    private Bitmap? _bitmap;
    private CancellationTokenSource? _load;
    private uint? _item;
    private bool _disposed;
    public Task Ready { get; private set; } = Task.CompletedTask;

    public void Show(SacredEquipment definition)
    {
        if (_item == definition.IdemId) return;
        Clear();
        _item = definition.IdemId;
        Width = Math.Max(32, definition.Width * 32); Height = Math.Max(32, definition.Height * 32);
        if (string.IsNullOrWhiteSpace(definition.Item.ModelName)) return;
        _load = new();
        Children.Add(_image);
        Ready = LoadAsync(definition, _load.Token);
    }

    private async Task LoadAsync(SacredEquipment definition, CancellationToken token)
    {
        try
        {
            var preview = await renderer.RenderAsync(definition, new((float)Width, (float)Height), false, token);
            if (_disposed || token.IsCancellationRequested) { preview.Bitmap.Dispose(); return; }
            _bitmap = preview.Bitmap;
            _image.Source = _bitmap; _image.Width = preview.Size.X; _image.Height = preview.Size.Y;
            SetLeft(_image, preview.Offset.X); SetTop(_image, preview.Offset.Y);
            Console.WriteLine($"[Drops] In-frame model ready: {definition.IdemId}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Clear()
    {
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _image.Source = null; _bitmap?.Dispose(); _bitmap = null;
        Children.Clear(); _item = null;
        Width = Height = 0;
        Ready = Task.CompletedTask;
    }

    public void Dispose() { if (_disposed) return; _disposed = true; Clear(); }
}
