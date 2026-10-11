using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Sacred.Core.Pak.Weapon;
using Sacred.UI.Textures;

namespace AssetViewer.AssetViewer;

/// <summary>A clickable native slot with a separately rendered item layer; no hover tooltip.</summary>
internal sealed class EquipmentSlotControl : UserControl, IDisposable
{
    private readonly Image _item = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Border _selection = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly UiTextureRegion _art;
    private readonly EquipmentSlotPreviewRenderer _previews;
    private readonly bool _weaponSlot;
    private readonly Image? _emptyBackground;
    private Bitmap? _bitmap;
    private CancellationTokenSource? _load;
    private Guid? _instance;
    private bool _disposed;
    public CharacterSlotRow Row { get; }
    public Task Ready { get; private set; } = Task.CompletedTask;
    public event Action<CharacterSlotRow>? Selected;

    public EquipmentSlotControl(CharacterSlotRow row, UiTextureRegion art, EquipmentPanelArtwork artwork,
        EquipmentSlotPreviewRenderer previews, UiTextureRegion? emptyBackground = null)
    {
        Row = row;
        _art = art;
        _previews = previews;
        _weaponSlot = emptyBackground is not null;
        Width = art.Size.X; Height = art.Size.Y;
        var layers = new Grid();
        if (emptyBackground is not null)
        {
            _emptyBackground = new Image { Source = artwork.Get(emptyBackground), Stretch = Stretch.Fill,
                IsHitTestVisible = false, IsVisible = Row.Slot.Instance is null };
            layers.Children.Add(_emptyBackground);
        }
        layers.Children.Add(new Image { Source = artwork.Get(art), Stretch = Stretch.Fill,
            Opacity = _weaponSlot ? 1 : 175d / 255 });
        var itemLayer = new Canvas { IsHitTestVisible = false };
        itemLayer.Children.Add(_item);
        layers.Children.Add(itemLayer);
        layers.Children.Add(_selection);
        Content = layers;
        PointerPressed += (_, e) => { Selected?.Invoke(Row); e.Handled = true; };
        AutomationProperties.SetName(this, row.Type.ToString());
        Refresh();
    }

    public void SetSelected(bool selected) => _selection.BorderBrush = selected ? Brushes.Goldenrod : Brushes.Transparent;

    public void Refresh()
    {
        if (_emptyBackground is not null) _emptyBackground.IsVisible = Row.Slot.Instance is null;
        if (_instance == Row.Slot.Instance?.InstanceId) return;
        _instance = Row.Slot.Instance?.InstanceId;
        _load?.Cancel();
        _load?.Dispose();
        _load = new();
        _item.Source = null;
        _bitmap?.Dispose(); _bitmap = null;
        Ready = Row.Slot.Equipment is { Item.ModelName.Length: > 0 } equipment
            ? LoadAsync(equipment, _load.Token) : Task.CompletedTask;
    }

    private async Task LoadAsync(SacredEquipment equipment, CancellationToken token)
    {
        try
        {
            var preview = await _previews.RenderAsync(equipment, _art.Size, _weaponSlot, token);
            if (_disposed || token.IsCancellationRequested) { preview.Bitmap.Dispose(); return; }
            _bitmap = preview.Bitmap;
            _item.Width = preview.Size.X; _item.Height = preview.Size.Y;
            Canvas.SetLeft(_item, preview.Offset.X); Canvas.SetTop(_item, preview.Offset.Y);
            _item.Source = preview.Bitmap;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.WriteLine($"[Assets] Equipment slot preview unavailable: {equipment.IdemId}; {error.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _load?.Cancel();
        _item.Source = null;
        _bitmap?.Dispose();
    }
}
