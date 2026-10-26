using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

/// <summary>Hosts weapon quickslots independently of the inventory panel and its armor layout.</summary>
internal sealed class CharacterQuickSlotHost : UserControl, IDisposable
{
    private readonly Task _loading;
    private readonly EquipmentSlotPreviewRenderer _previews;
    private EquipmentPanelAssets? _assets;
    private EquipmentPanelArtwork? _artwork;
    private CharacterQuickSlotPane? _quickslot;
    private CharacterSlotRow[] _rows = [];
    private CharacterSlotRow? _selected;
    private bool _disposed;
    public event Action<CharacterSlotRow>? Selected;
    public Task Ready => AwaitReadyAsync();

    public CharacterQuickSlotHost(AssetViewerSession session)
    {
        _previews = new(session);
        HorizontalAlignment = HorizontalAlignment.Center;
        Margin = new(8, 16);
        _loading = LoadAsync(session);
    }

    public void SetActor(CharacterSlotRow[] rows)
    { _rows = rows; Rebuild(); }

    public void Refresh(CharacterSlotRow? selected)
    { _selected = selected; _quickslot?.SetSelected(selected); _quickslot?.Refresh(); }

    private async Task LoadAsync(AssetViewerSession session)
    {
        try
        {
            _assets = await EquipmentPanelAssets.LoadAsync(session.GameDirectory, name => session.TextureCache.LoadTextureAsync(name));
            if (_disposed) return;
            _artwork = new(_assets); Rebuild();
            Console.WriteLine("[Assets] Weapon quickslot loaded: one native frame; main hand then offhand.");
        }
        catch (Exception error) { Console.WriteLine($"[Assets] Weapon quickslot unavailable: {error.Message}"); }
    }

    private void Rebuild()
    {
        if (_assets is null || _artwork is null || _rows.Length == 0) return;
        _quickslot?.Dispose();
        _quickslot = new(_rows, _assets, _artwork, _previews);
        _quickslot.Selected += row => Selected?.Invoke(row);
        Content = _quickslot; Refresh(_selected);
    }

    private async Task AwaitReadyAsync() { await _loading; if (_quickslot is { } quickslot) await quickslot.Ready; }

    public void Dispose()
    {
        _disposed = true; _quickslot?.Dispose(); _artwork?.Dispose();
        _ = Ready.ContinueWith(_ => Dispatcher.UIThread.Post(_previews.Dispose), TaskScheduler.Default);
    }
}
