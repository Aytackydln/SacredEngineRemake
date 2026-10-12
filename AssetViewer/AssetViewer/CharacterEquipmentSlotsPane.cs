using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Sacred.Inventory.Actors;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

/// <summary>Hosts only equipment slots; stats and weapon quickslots are independent components.</summary>
internal sealed class CharacterEquipmentSlotsPane : UserControl, IDisposable
{
    private readonly StackPanel _panel = new() { Spacing = 8 };
    private readonly TextBlock _selected = new() { TextWrapping = TextWrapping.Wrap };
    private readonly EquipmentSlotPreviewRenderer _previews;
    private readonly Task _loading;
    private EquipmentPanelAssets? _assets;
    private EquipmentPanelArtwork? _artwork;
    private CharacterArmorSlotsPane? _armor;
    private CharacterSlotRow[] _rows = [];
    private SacredCharacterClass? _class;
    private bool _disposed;
    public CharacterSlotRow? Selected { get; private set; }
    public event Action<CharacterSlotRow>? SelectionChanged;
    public Task Ready => AwaitReadyAsync();

    public CharacterEquipmentSlotsPane(AssetViewerSession session)
    {
        _previews = new(session);
        Content = new ScrollViewer { Content = _panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _panel.Children.Add(new TextBlock { Text = "Equipment slots", FontWeight = FontWeight.Bold });
        _panel.Children.Add(new TextBlock { Text = "Loading original equipment artwork…" });
        _loading = LoadAsync(session);
    }

    public void SetActor(CharacterSlotRow[] rows, SacredCharacterClass? characterClass)
    {
        _rows = rows;
        _class = characterClass;
        Selected = rows.FirstOrDefault();
        Rebuild();
        if (Selected is { } row) Select(row);
    }

    public void Select(CharacterSlotRow row)
    {
        Selected = row;
        Refresh();
        SelectionChanged?.Invoke(row);
        Console.WriteLine($"[Assets] Equipment slot selected: {row.Index}; {row.Type}; occurrence {row.Occurrence + 1}.");
    }

    public void Refresh()
    {
        foreach (var slot in Controls()) { slot.SetSelected(slot.Row == Selected); slot.Refresh(); }
        _selected.Text = Selected?.ToString() ?? "Select a character.";
    }

    public void PrintLayout()
    {
        if (_assets is null) throw new InvalidOperationException("Equipment artwork is not ready.");
        if (_class is { } characterClass)
            foreach (var slot in new EquipmentSlotsLayout(characterClass, _assets.Definitions).Slots)
                Console.WriteLine($"[Equipment layout] {slot.Type} {slot.Occurrence}: native {slot.NativeIndex}; " +
                    $"position {slot.Position}; size {slot.Art.Size}; selector {slot.Art.Authored.Definition}; {slot.Art.TextureName}.");
        Console.WriteLine($"[Equipment layout] {_class?.ToString() ?? "NPC"}: {Controls().Length} controls; selected {Selected?.Index}.");
    }

    private EquipmentSlotControl[] Controls() => (_armor?.Slots ?? []).ToArray();

    private async Task LoadAsync(AssetViewerSession session)
    {
        try
        {
            _assets = await EquipmentPanelAssets.LoadAsync(session.GameDirectory,
                name => session.Textures.LoadTextureAsync(name));
            if (_disposed) return;
            _artwork = new(_assets);
            Rebuild();
            Refresh();
            Console.WriteLine($"[Assets] Equipment panel artwork loaded: {_assets.Textures.Count} original atlases.");
        }
        catch (Exception error)
        {
            if (_disposed) return;
            _panel.Children.Clear();
            _panel.Children.Add(new TextBlock { Text = $"Equipment artwork unavailable: {error.Message}", TextWrapping = TextWrapping.Wrap });
            Console.WriteLine($"[Assets] Equipment artwork unavailable: {error.Message}");
        }
    }

    private void Rebuild()
    {
        if (_assets is null || _artwork is null || _rows.Length == 0) return;
        _armor?.Dispose(); _armor = null;
        _panel.Children.Clear();
        _panel.Children.Add(new TextBlock { Text = "Equipment slots", FontWeight = FontWeight.Bold });
        if (_class is { } characterClass)
        {
            _armor = new(characterClass, _rows, _assets, _artwork, _previews);
            _panel.Children.Add(new Viewbox { Child = _armor, Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        _panel.Children.Add(_selected);
        foreach (var slot in Controls()) slot.Selected += Select;
        Console.WriteLine($"[Assets] Equipment layout: {_class?.ToString() ?? "NPC"}; {_armor?.Slots.Count ?? 0} armor/accessory controls; slots only; hands hosted by quickslot.");
    }

    private async Task AwaitReadyAsync()
    {
        await _loading;
        await Task.WhenAll(Controls().Select(s => s.Ready));
    }

    public void Dispose()
    {
        _disposed = true;
        _armor?.Dispose();
        _artwork?.Dispose();
        _ = Ready.ContinueWith(_ => _previews.Dispose(), TaskScheduler.FromCurrentSynchronizationContext());
    }
}
