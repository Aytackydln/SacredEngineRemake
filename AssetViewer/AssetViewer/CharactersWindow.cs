using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Sacred.Core.Pak.Items;
using Sacred.Inventory.Actors;

namespace AssetViewer.AssetViewer;

internal sealed class CharactersWindow : Window, IAssetViewerWindow
{
    private readonly AssetViewerSession _session;
    private readonly AssetTableControl<ModelAssetRow> _characters;
    private readonly ModelPreviewPane _preview;
    private readonly ComboBox _profile = new() { MinWidth = 230 };
    private readonly ComboBox _animation = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _playPause = new() { Content = "Pause" };
    private CharacterAnimationChoice[] _animations = [];
    private bool _updatingAnimations;
    private bool _playing = true;
    private readonly ListBox _inventory = new() { MinHeight = 180 };
    private readonly ListBox _matches = new() { MinHeight = 160 };
    private readonly TextBox _search = new() { PlaceholderText = "Search equippable item by name or ID" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _equip = new() { Content = "Equip", IsEnabled = false };
    private CharacterSlotRow[] _slots = [];
    private readonly CharacterEquipmentRow[] _equipment;
    private SacredCharacterClass? Class => _profile.SelectedIndex > 0 ? (SacredCharacterClass)_profile.SelectedItem! : null;

    public CharactersWindow(AssetViewerSession session)
    {
        _session = session;
        Title = "Sacred Asset Viewer · Characters";
        Width = 1600; Height = 950;
        _characters = new(session.Items.Where(item => item.ModelDesc.Category == SacredItemCategory.Creature)
            .Select(item => new ModelAssetRow(item)), row => $"{row.EntryId} {row.ResourceId} {row.Model}");
        _equipment = session.Equipment.Select(item => new CharacterEquipmentRow(item,
            session.Data.GameResStore.GetString(item.IdemId.ToString(CultureInfo.InvariantCulture), item.Name))).ToArray();
        _preview = new(session);
        _profile.ItemsSource = new object[] { "NPC · unrestricted class" }.Concat(Enum.GetValues<SacredCharacterClass>().Cast<object>()).ToArray();
        _profile.SelectionChanged += (_, _) => ResetInventory();
        _profile.SelectedIndex = 0;
        var inventoryPanel = new Grid { RowDefinitions = new("Auto,Auto,2*,Auto,2*,Auto,Auto"), Margin = new Thickness(8, 0, 0, 0) };
        Add(inventoryPanel, new TextBlock { Text = "Inventory layout / class restriction" }, 0);
        Add(inventoryPanel, _profile, 1);
        Add(inventoryPanel, _inventory, 2);
        Add(inventoryPanel, _search, 3);
        Add(inventoryPanel, _matches, 4);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_equip);
        var unequip = new Button { Content = "Unequip" }; buttons.Children.Add(unequip);
        var clear = new Button { Content = "Clear all" }; buttons.Children.Add(clear);
        Add(inventoryPanel, buttons, 5);
        Add(inventoryPanel, _status, 6);
        var root = new Grid { ColumnDefinitions = new("3*,5,4*,3*"), Margin = new Thickness(12) };
        root.Children.Add(_characters);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1); root.Children.Add(splitter);
        var previewPanel = new DockPanel();
        var animationBar = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        animationBar.Children.Add(new TextBlock { Text = "Animation ", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_animation, 1); animationBar.Children.Add(_animation);
        Grid.SetColumn(_playPause, 2); animationBar.Children.Add(_playPause);
        DockPanel.SetDock(animationBar, Dock.Top); previewPanel.Children.Add(animationBar);
        previewPanel.Children.Add(_preview);
        Grid.SetColumn(previewPanel, 2); root.Children.Add(previewPanel);
        Grid.SetColumn(inventoryPanel, 3); root.Children.Add(inventoryPanel);
        Content = root;
        _characters.SelectedChanged += _ => { RefreshAnimations(); ResetInventory(); };
        _animation.SelectionChanged += (_, _) => { if (!_updatingAnimations) RebuildPreview(); };
        _playPause.Click += (_, _) => SetAnimationPlaying(!_playing);
        _inventory.SelectionChanged += (_, _) => FilterEquipment();
        _search.TextChanged += (_, _) => FilterEquipment();
        _matches.SelectionChanged += (_, _) => _equip.IsEnabled = _matches.SelectedItem is CharacterEquipmentRow && _characters.Selected is { Model.Length: > 0 };
        _equip.Click += (_, _) => { if (_matches.SelectedItem is CharacterEquipmentRow item && _inventory.SelectedItem is CharacterSlotRow slot) Equip(slot.Index, item.EntryId); };
        unequip.Click += (_, _) => { if (_inventory.SelectedItem is CharacterSlotRow slot) Unequip(slot.Index); };
        clear.Click += (_, _) => { foreach (var slot in _slots) slot.Slot.Unequip(); RefreshInventory(); RebuildPreview(); };
        Closed += (_, _) => _preview.Cancel();
        ResetInventory();
    }

    public Task Ready => _preview.CurrentLoad;
    public void Select(ushort id) => _characters.Select(row => row.EntryId == id);
    public void SetClass(string name)
    {
        _profile.SelectedItem = name.Equals("npc", StringComparison.OrdinalIgnoreCase) ? "NPC · unrestricted class" : Enum.Parse<SacredCharacterClass>(name, true);
    }
    public void SaveScreenshot(string path) => _preview.SaveScreenshot(path);
    public void RotateHorizontally(float radians) => _preview.RotateHorizontally(radians);

    public void ListAnimations()
    {
        foreach (var animation in _animations)
            Console.WriteLine($"[Assets] Animation: {animation.Name}" + (animation.Slot is { } slot ? $" (slot {slot})" : string.Empty));
    }

    public void SelectAnimation(string name)
    {
        if (_characters.Selected is { } character && byte.TryParse(name, out var motionSlot) &&
            _session.Models.TryGetModelMotionName(character.Model, motionSlot, out var motionName)) name = motionName;
        var selected = name.Equals("bind", StringComparison.OrdinalIgnoreCase) ? CharacterAnimationChoice.BindPose :
            name.Equals("default", StringComparison.OrdinalIgnoreCase) ? CharacterAnimationChoice.Default :
            _animations.FirstOrDefault(choice => choice.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException($"Animation '{name}' is unavailable. Use 'animations' to list this character's clips.");
        if (_animations.Length == 0) throw new InvalidOperationException("Select a character first.");
        _animation.SelectedItem = selected;
    }

    public void SetAnimationPlaying(bool playing)
    {
        _playing = playing;
        _playPause.Content = playing ? "Pause" : "Play";
        _preview.SetAnimationPlaying(playing);
        Console.WriteLine($"[Assets] Animation {(playing ? "playing" : "paused")}.");
    }

    public void SetAnimationTime(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        SetAnimationPlaying(false);
        _preview.SetAnimationTime(seconds);
    }

    private void RefreshAnimations()
    {
        _updatingAnimations = true;
        _animations = _characters.Selected is { Model.Length: > 0 } character
            ? CharacterAnimationChoice.ForModel(_session.Models, character.Model) : [];
        _animation.ItemsSource = _animations;
        _animation.SelectedItem = _animations.Length > 0 ? CharacterAnimationChoice.Default : null;
        _updatingAnimations = false;
    }

    public void Equip(int slotIndex, uint itemId)
    {
        if (_characters.Selected is not { Model.Length: > 0 }) throw new InvalidOperationException("Select a character with a model.");
        var target = _slots.Single(slot => slot.Index == slotIndex);
        var item = _equipment.Single(item => item.EntryId == itemId);
        if (!CharacterEquipment.CanEquip(target.Type, Class, item.Item)) throw new InvalidOperationException($"Item {itemId} is not equippable in {target.Type} for this class.");
        CharacterEquipment.Equip(_slots, target, item.Item);
        RefreshInventory(target);
        Console.WriteLine($"[Assets] Equipped {itemId} in slot {slotIndex} ({target.Type}).");
        RebuildPreview();
    }

    public void Unequip(int index)
    {
        var target = _slots.Single(slot => slot.Index == index);
        target.Slot.Unequip(); RefreshInventory(target); RebuildPreview();
        Console.WriteLine($"[Assets] Unequipped slot {index}.");
    }

    private void ResetInventory()
    {
        var slots = Class is { } characterClass ? new SacredGameActor(characterClass).EquipmentSlots.ToArray() :
            Enum.GetValues<EquipmentSlotType>().Where(type => type != EquipmentSlotType.SmallBelt).Select(type => new EquipmentSlot(type)).ToArray();
        _slots = slots.Select((slot, index) => new CharacterSlotRow(index, slot)).ToArray();
        RefreshInventory();
        RebuildPreview();
    }

    private void RefreshInventory(CharacterSlotRow? selected = null)
    {
        _inventory.ItemsSource = null;
        _inventory.ItemsSource = _slots;
        _inventory.SelectedItem = selected ?? _slots.FirstOrDefault();
        FilterEquipment();
    }

    private void FilterEquipment()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        var matches = _inventory.SelectedItem is CharacterSlotRow slot ? _equipment.Where(item =>
            CharacterEquipment.CanEquip(slot.Type, Class, item.Item) && item.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray() : [];
        _matches.ItemsSource = matches;
        _status.Text = $"{matches.Length:N0} equippable items. Choose an inventory layout to apply player class restrictions. Jewelry occupies a slot without adding a mesh.";
        _equip.IsEnabled = false;
    }

    private void RebuildPreview()
    {
        if (_characters.Selected is not { } character) return;
        if (string.IsNullOrWhiteSpace(character.Model)) { _preview.ShowStatus($"Creature row {character.EntryId} has no 3D model."); return; }
        var attachments = _slots.Where(slot => slot.Type is not (EquipmentSlotType.Ring or EquipmentSlotType.Amulet) &&
            slot.Slot.Equipment is { Item.ModelName.Length: > 0 }).ToArray();
        var references = attachments.Select(CharacterEquipment.Attachment).ToArray();
        var visuals = new[] { new ModelPreviewVisual(character.Item) }.Concat(attachments.Select((slot, index) =>
            new ModelPreviewVisual(slot.Slot.Equipment!.Value.Item, slot.Slot.Equipment,
                references[index].RigidAttachBoneName))).ToArray();
        var animation = _animation.SelectedItem as CharacterAnimationChoice ?? CharacterAnimationChoice.BindPose;
        _ = _preview.LoadAsync(character.Model, async token =>
        {
            var model = await _session.Models.LoadCharacterBaseModelAsync(character.Model, references, token);
            var clip = model.Skin is null ? null : await animation.LoadAsync(_session.Models, character.Model, token);
            Console.WriteLine($"[Assets] Character animation: {clip?.Name ?? "bind pose"}; requested {animation.Name}; {clip?.DurationSeconds ?? 0:F3}s.");
            return model with { DefaultAnimation = clip };
        }, visuals);
    }

    private static void Add(Grid grid, Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
}
