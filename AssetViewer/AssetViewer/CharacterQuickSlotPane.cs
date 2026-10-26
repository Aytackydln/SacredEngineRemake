using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Sacred.Inventory.Actors;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

/// <summary>The taskbar's single weapon frame, with independently refreshed main-hand and offhand layers.</summary>
internal sealed class CharacterQuickSlotPane : UserControl, IDisposable
{
    private readonly CharacterSlotRow[] _rows;
    private readonly EquipmentPanelAssets _assets;
    private readonly EquipmentSlotPreviewRenderer _previews;
    private readonly Canvas _items = new();
    private readonly Image _empty;
    private readonly Border _selection = new() { BorderThickness = new(1), IsHitTestVisible = false };
    private readonly List<Bitmap> _bitmaps = [];
    private CancellationTokenSource? _loading;
    private Guid? _mainId;
    private Guid? _offId;
    private bool _initialized;
    private bool _disposed;
    public Task Ready { get; private set; } = Task.CompletedTask;
    public event Action<CharacterSlotRow>? Selected;

    public CharacterQuickSlotPane(CharacterSlotRow[] rows, EquipmentPanelAssets assets,
        EquipmentPanelArtwork artwork, EquipmentSlotPreviewRenderer previews)
    {
        _rows = rows; _assets = assets; _previews = previews;
        Width = assets.QuickSlot.Frame.Size.X; Height = assets.QuickSlot.Frame.Size.Y;
        // The artwork and item layers ignore input; keep the entire empty/filled frame clickable.
        var layers = new Grid { Background = Brushes.Transparent };
        _empty = new Image { Source = artwork.Get(assets.QuickSlot.Empty), Stretch = Stretch.Fill, IsHitTestVisible = false };
        layers.Children.Add(_empty);
        layers.Children.Add(new Image { Source = artwork.Get(assets.QuickSlot.Frame), Stretch = Stretch.Fill, IsHitTestVisible = false });
        _items.IsHitTestVisible = false; layers.Children.Add(_items); layers.Children.Add(_selection);
        Content = layers;
        PointerPressed += (_, e) =>
        {
            var hand = e.GetCurrentPoint(this).Properties.IsRightButtonPressed
                ? EquipmentSlotType.LeftHand : EquipmentSlotType.RightHand;
            Selected?.Invoke(_rows.Single(r => r.Type == hand)); e.Handled = true;
        };
        ToolTip.SetTip(this, "Weapon quickslot · left-click selects main hand; right-click selects offhand.");
        Refresh();
    }

    public void SetSelected(CharacterSlotRow? row) => _selection.BorderBrush =
        row?.Type is EquipmentSlotType.RightHand or EquipmentSlotType.LeftHand ? Brushes.Goldenrod : Brushes.Transparent;

    public void Refresh()
    {
        var main = _rows.Single(r => r.Type == EquipmentSlotType.RightHand).Slot.Instance;
        var off = _rows.Single(r => r.Type == EquipmentSlotType.LeftHand).Slot.Instance;
        _empty.IsVisible = main is null && off is null;
        if (_initialized && _mainId == main?.InstanceId && _offId == off?.InstanceId) return;
        _initialized = true; _mainId = main?.InstanceId; _offId = off?.InstanceId;
        _loading?.Cancel(); _loading?.Dispose(); _loading = new();
        _items.Children.Clear();
        foreach (var bitmap in _bitmaps) bitmap.Dispose(); _bitmaps.Clear();
        var hands = WeaponQuickSlotLayout.Items(main?.Definition, off?.Definition).ToArray();
        Ready = LoadAsync(hands, _loading.Token);
    }

    private async Task LoadAsync(WeaponQuickSlotItem[] hands, CancellationToken token)
    {
        foreach (var hand in hands)
        {
            if (string.IsNullOrWhiteSpace(hand.Equipment.Item.ModelName)) continue;
            try
            {
                var preview = await _previews.RenderAsync(hand.Equipment, _assets.QuickSlot.Frame.Size, true, token);
                if (_disposed || token.IsCancellationRequested) { preview.Bitmap.Dispose(); return; }
                _bitmaps.Add(preview.Bitmap);
                var image = new Image { Source = preview.Bitmap, Width = preview.Size.X,
                    Height = preview.Size.Y, Stretch = Stretch.Fill, IsHitTestVisible = false };
                Canvas.SetLeft(image, preview.Offset.X + hand.Offset.X);
                Canvas.SetTop(image, preview.Offset.Y + hand.Offset.Y);
                _items.Children.Add(image);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) { Console.WriteLine($"[Assets] Weapon quickslot preview unavailable: {hand.Equipment.IdemId}; {error.Message}"); }
        }
    }

    public void Dispose()
    {
        _disposed = true; _loading?.Cancel(); _loading?.Dispose();
        _items.Children.Clear();
        foreach (var bitmap in _bitmaps) bitmap.Dispose(); _bitmaps.Clear();
    }
}
