using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Items;
using Sacred.Inventory.Stats;

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
    private readonly CharacterEquipmentSlotsPane _equipmentSlots;
    private readonly CharacterBackpackPane _backpack;
    private CharacterSlotRow[] _slots = [];
    private readonly CharacterEquipmentRow[] _equipment;
    private readonly CharacterStatsPane _stats;
    private readonly CharacterCombatArtPresentation _artPresentation;
    private readonly CharacterItemDescriptionPane _description;
    private readonly Grid _root;
    private readonly GridSplitter _splitter;
    private readonly TextBlock _selectedCharacter = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _changeCharacter = new() { Content = "Change character" };
    private readonly Dictionary<ushort, SacredGameActor> _actors = new();
    private SacredGameActor? _actor;
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
        _equipmentSlots = new(session);
        _backpack = new(_equipment);
        _preview = new(session);
        _profile.ItemsSource = new object[] { "NPC · unrestricted class" }.Concat(Enum.GetValues<SacredCharacterClass>().Cast<object>()).ToArray();
        _stats = new(session.Data.GameResStore);
        _artPresentation = new(session);
        _stats.ConfigureArtPresentation(_artPresentation);
        _description = new(session);
        _profile.SelectionChanged += (_, _) => FilterEquipment();
        _profile.SelectedIndex = 0;
        var inventoryPanel = new Grid { RowDefinitions = new("Auto,Auto,*"), Margin = new Thickness(8, 0, 8, 0) };
        Add(inventoryPanel, new TextBlock { Text = "Equipment class filter" }, 0);
        Add(inventoryPanel, _profile, 1);
        Add(inventoryPanel, _equipmentSlots, 2);
        var backpackPanel = new Grid { RowDefinitions = new("2*,3*"), Margin = new Thickness(8, 0, 8, 0) };
        Add(backpackPanel, _backpack, 0);
        Add(backpackPanel, _description, 1);
        _root = new Grid { ColumnDefinitions = new("3*,5,4*,3*,4*,4*"), Margin = new Thickness(12) };
        _root.Children.Add(_characters);
        _splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(_splitter, 1); _root.Children.Add(_splitter);
        var previewPanel = new DockPanel();
        var animationBar = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        animationBar.Children.Add(new TextBlock { Text = "Animation ", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_animation, 1); animationBar.Children.Add(_animation);
        Grid.SetColumn(_playPause, 2); animationBar.Children.Add(_playPause);
        DockPanel.SetDock(animationBar, Dock.Top); previewPanel.Children.Add(animationBar);
        previewPanel.Children.Add(_preview);
        Grid.SetColumn(previewPanel, 2); _root.Children.Add(previewPanel);
        Grid.SetColumn(inventoryPanel, 3); _root.Children.Add(inventoryPanel);
        Grid.SetColumn(backpackPanel, 4); _root.Children.Add(backpackPanel);
        Grid.SetColumn(_stats, 5); _root.Children.Add(_stats);
        var banner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12, 8) };
        banner.Children.Add(_changeCharacter); banner.Children.Add(_selectedCharacter);
        var shell = new DockPanel(); DockPanel.SetDock(banner, Dock.Top); shell.Children.Add(banner); shell.Children.Add(_root);
        Content = shell;
        _changeCharacter.Click += (_, _) => ShowCharacterList(!_characters.IsVisible);
        _characters.SelectedChanged += character =>
        {
            RefreshAnimations(); ResetInventory();
            var mask = _actor?.Progression.Template?.PlayableClassMask ?? SacredCharacterClassMask.None;
            _profile.SelectedItem = Enum.GetValues<SacredCharacterClass>().Where(value => value.ToMask() == mask)
                .Cast<object>().FirstOrDefault() ?? _profile.Items[0];
            _selectedCharacter.Text = $"Character {character.EntryId} · {character.Model}";
            ShowCharacterList(false);
            Console.WriteLine($"[Assets] Character selected: {character.EntryId}; model {character.Model}; template {_actor?.Progression.Template?.RecordIndex.ToString() ?? "unmapped"}; class filter {_profile.SelectedItem}.");
        };
        _animation.SelectionChanged += (_, _) => { if (!_updatingAnimations) RebuildPreview(); };
        _playPause.Click += (_, _) => SetAnimationPlaying(!_playing);
        _equipmentSlots.SelectionChanged += _ => FilterEquipment();
        _backpack.SelectionChanged += RefreshDescription;
        _backpack.EquipRequested += (slot, item) => Equip(slot.Index, item.EntryId);
        _backpack.UnequipRequested += slot => Unequip(slot.Index);
        _backpack.ClearRequested += ClearInventory;
        Closed += (_, _) => { _preview.Cancel(); _description.Cancel(); _artPresentation.Dispose(); _equipmentSlots.Dispose(); };
        ResetInventory();
    }

    public Task Ready => Task.WhenAll(_preview.CurrentLoad, _description.Ready, _artPresentation.Ready, _equipmentSlots.Ready);
    public void Select(ushort id) => _characters.Select(row => row.EntryId == id);
    public void SetClass(string name)
    {
        _profile.SelectedItem = name.Equals("npc", StringComparison.OrdinalIgnoreCase) ? "NPC · unrestricted class" : Enum.Parse<SacredCharacterClass>(name, true);
    }
    public void SaveScreenshot(string path) => _preview.SaveScreenshot(path);
    public void SaveItemScreenshot(string path) => _description.SaveScreenshot(path);
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
        _actor!.Equip(slotIndex, SacredItemInstance.FromDefinition(item.Item));
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
        if (_characters.Selected is not { } character) return;
        if (!_actors.TryGetValue(character.EntryId, out _actor))
        {
            var templates = _session.Data.Creatures;
            var template = templates?.ResolveTemplate(character.Item);
            var characterClass = ResolveClass(template?.PlayableClassMask ?? SacredCharacterClassMask.None);
            _actor = characterClass is { } playable ? new SacredGameActor(playable, template)
                : new SacredGameActor(template, [EquipmentSlotType.LeftHand, EquipmentSlotType.RightHand]);
            _actor.Progression.ConfigureCombatArts(_session.Data.CombatArts.Entries);
            _actors.Add(character.EntryId, _actor);
        }
        var occurrences = new Dictionary<EquipmentSlotType, int>();
        _slots = _actor.EquipmentSlots.Select((slot, index) =>
        {
            var occurrence = occurrences.GetValueOrDefault(slot.Type);
            occurrences[slot.Type] = occurrence + 1;
            return new CharacterSlotRow(index, slot) { Occurrence = occurrence };
        }).ToArray();
        _equipmentSlots.SetActor(_slots, ResolveClass(_actor.Progression.Template?.PlayableClassMask ?? SacredCharacterClassMask.None));
        _stats.SelectActor(_actor);
        RefreshInventory();
        RebuildPreview();
    }

    private void RefreshInventory(CharacterSlotRow? selected = null)
    {
        _equipmentSlots.Refresh();
        if (selected is not null) _equipmentSlots.Select(selected);
        FilterEquipment();
    }

    private void FilterEquipment()
    {
        _backpack.Configure(_equipmentSlots.Selected, Class);
        RefreshDescription();
    }

    private void RefreshDescription()
    {
        if (_backpack.Selected is { } candidate) _description.Show(candidate.Item);
        else if (_equipmentSlots.Selected is { Slot.Instance: { } instance }) _description.Show(instance);
        else _description.Clear();
    }

    public void SelectInventorySlot(int index) => _equipmentSlots.Select(_slots.Single(slot => slot.Index == index));
    public void SelectInventoryItem(uint itemId) => _backpack.Select(itemId);
    public void PrintSelectedDescription() => _description.PrintDescription();

    private void RebuildPreview()
    {
        if (_characters.Selected is not { } character) return;
        if (string.IsNullOrWhiteSpace(character.Model)) { _preview.ShowStatus($"Creature row {character.EntryId} has no 3D model."); return; }
        var attachments = _slots.Where(slot => slot.Slot.Equipment is { Item.ModelName.Length: > 0 }).ToArray();
        var references = attachments.Select(CharacterEquipment.Attachment).ToArray();
        foreach (var (slot, reference) in attachments.Zip(references))
            Console.WriteLine($"[Assets] Model slot {slot.Index}: {slot.Type} {slot.Occurrence + 1}; {reference.ModelName}; " +
                (reference.RigidAttachBoneName is { } bone ? $"{reference.SourceAttachBoneName} -> {bone}" : "wearable skeleton"));
        var visuals = new[] { new ModelPreviewVisual(character.Item) }.Concat(attachments.Select((slot, index) =>
            new ModelPreviewVisual(slot.Slot.Equipment!.Value.Item, slot.Slot.Equipment,
                references[index].RigidAttachBoneName))).ToArray();
        var animation = _animation.SelectedItem as CharacterAnimationChoice ?? CharacterAnimationChoice.BindPose;
        _ = _preview.LoadActorAsync(character.Model, async token =>
        {
            var model = await _session.Models.LoadCharacterBaseModelAsync(character.Model, references, token);
            var clip = model.Skin is null ? null : await animation.LoadAsync(_session.Models, character.Model, token);
            Console.WriteLine($"[Assets] Character animation: {clip?.Name ?? "bind pose"}; requested {animation.Name}; {clip?.DurationSeconds ?? 0:F3}s.");
            return model with { DefaultAnimation = clip };
        }, visuals);
    }

    private void ShowCharacterList(bool visible)
    {
        _characters.IsVisible = visible;
        _splitter.IsVisible = visible;
        _root.ColumnDefinitions[0].Width = visible ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        _root.ColumnDefinitions[1].Width = new GridLength(visible ? 5 : 0);
    }

    public void PrintStats() => _stats.PrintStats();
    public void PrintAllocation() => _stats.PrintAllocation();
    public void SelectStatsView(string name) => _stats.SelectView(name);
    public void PrintInventory()
    {
        foreach (var slot in _slots) Console.WriteLine($"[Inventory] {slot}; instance {slot.Slot.Instance?.InstanceId}");
        Console.WriteLine($"[Inventory] Character list visible: {_characters.IsVisible}; class filter: {_profile.SelectedItem}.");
    }
    public void PrintEquipmentLayout() => _equipmentSlots.PrintLayout();
    public void ClearInventory()
    {
        foreach (var slot in RequireActor().EquipmentSlots) slot.Unequip();
        RefreshInventory(); RebuildPreview();
        Console.WriteLine("[Assets] Inventory cleared.");
    }
    public void SetLevel(ushort level) => RequireActor().Progression.SetLevel(level);
    public void SetAttribute(string name, int points) => RequireActor().Progression.SetAllocatedAttribute(Enum.Parse<SacredActorStat>(name, true), points);
    public void SetSkill(byte id, int rank) => RequireActor().Progression.SetSkill(id, rank);
    public void SetCombatArt(ushort code, int rank) => RequireActor().Progression.SetCombatArt(code, rank);
    private SacredGameActor RequireActor() => _actor ?? throw new InvalidOperationException("Select a character first.");

    private static SacredCharacterClass? ResolveClass(SacredCharacterClassMask mask) =>
        Enum.GetValues<SacredCharacterClass>().Where(value => value.ToMask() == mask)
            .Select(value => (SacredCharacterClass?)value).FirstOrDefault();

    private static void Add(Grid grid, Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
}
