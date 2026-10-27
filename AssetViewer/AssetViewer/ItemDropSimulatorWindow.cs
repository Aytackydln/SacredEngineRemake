using System;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Sacred.Assets.Equipment;
using Sacred.Core.Loot;
using Sacred.GameplayHost.Equipment;
using Sacred.Inventory.Items;

namespace AssetViewer.AssetViewer;

internal sealed class ItemDropSimulatorWindow : Window, IAssetViewerWindow
{
    private readonly AssetViewerSession _session;
    private readonly ItemDropDescriptionPane _description;
    private readonly ComboBox _difficulty = new() { MinWidth = 140 };
    private readonly NumericUpDown _level = new() { Minimum = 1, Maximum = ushort.MaxValue, Increment = 1, Value = 20, FormatString = "0", MinWidth = 100 };
    private readonly ComboBox _category = new() { MinWidth = 260 };
    private readonly TextBlock _categoryInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _heroClass = new() { MinWidth = 160 };
    private readonly ComboBox _rarity = new() { MinWidth = 230 };
    private readonly TextBox _seed = new() { Text = "2004", MinWidth = 130 };
    private readonly TextBlock _status = new() { Text = "Loading original item generation data…", TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<ItemDropHistoryRow> _history = new();
    private readonly ListBox _rolls = new();
    private readonly WrapPanel _actions = new() { Orientation = Orientation.Horizontal, IsEnabled = false };
    private readonly Task _loading;
    private readonly ItemDropRarityContextPane _rarityContext;
    private readonly ItemDropAuthoredNamePane _authoredNames;
    private SacredEquipmentGenerationData? _generationData;
    private WorldEquipmentDropSimulator? _simulator;
    private bool _closed;
    private int _number;

    private sealed record ClassChoice(string Name, uint Code) { public override string ToString() => Name; }

    public ItemDropSimulatorWindow(AssetViewerSession session)
    {
        _session = session;
        _rarityContext = new(session);
        _authoredNames = new(session, async type => { try { await InspectAuthoredAsync(type); } catch (Exception error) { ShowError(error); } });
        _description = new(session);
        _description.Clear("Roll an item, or select one from the history.", "Roll an item to preview.");
        Title = "Sacred Asset Viewer · Item drop simulator";
        Width = 1450; Height = 900; MinWidth = 1000; MinHeight = 650;
        _difficulty.ItemsSource = new[] { "Bronze", "Silver", "Gold", "Platinum", "Niobium" };
        _difficulty.SelectedIndex = 0;
        _category.ItemsSource = ItemDropCategoryChoice.All;
        _category.SelectionChanged += (_, _) => _categoryInfo.Text = (_category.SelectedItem as ItemDropCategoryChoice)?.Description;
        _category.SelectedIndex = 0;
        ToolTip.SetTip(_category, "Choose equal categories, an original category-weight preset, or a fixed category.");
        _heroClass.ItemsSource = new[] { new ClassChoice("Any class", 0), new("Seraphim", 1), new("Gladiator", 2),
            new("Battle Mage", 3), new("Dark Elf", 4), new("Wood Elf", 5), new("Vampiress (day)", 6),
            new("Vampiress (night)", 7), new("Dwarf", 8), new("Daemon", 9) };
        _heroClass.SelectedIndex = 0;
        _rarity.ItemsSource = ItemDropRarityChoice.All; _rarity.SelectedIndex = 0;
        ToolTip.SetTip(_rarity, "Random uses the selected rarity rules below. Fixed quality 0..14 overrides the rarity draw. Final rarity also depends on generated bonuses and sockets.");
        ToolTip.SetTip(_heroClass, "Native weighted categories use class eligibility. Within a category this is the original class preference, not a guaranteed equipment fit.");
        var inputs = new WrapPanel { Orientation = Orientation.Horizontal };
        inputs.Children.Add(Field("Difficulty *", _difficulty)); inputs.Children.Add(Field("Hero level *", _level));
        inputs.Children.Add(Field("Category", _category)); inputs.Children.Add(Field("Hero class", _heroClass));
        inputs.Children.Add(Field("Rarity", _rarity));
        inputs.Children.Add(Field("Seed", _seed));
        var newRoll = new Button { Content = "Roll new drop" };
        var rollSeed = new Button { Content = "Roll seed" };
        var replay = new Button { Content = "Replay selected" };
        var clear = new Button { Content = "Clear drops" };
        newRoll.Click += async (_, _) => await TryRollAsync(true);
        rollSeed.Click += async (_, _) => await TryRollAsync(false);
        replay.Click += async (_, _) => { try { await ReplayAsync(); } catch (Exception error) { ShowError(error); } };
        clear.Click += (_, _) => ClearDrops();
        _actions.Children.Add(newRoll); _actions.Children.Add(rollSeed); _actions.Children.Add(replay); _actions.Children.Add(clear);
        foreach (var choice in ItemDropCategoryChoice.All.Where(choice => choice.Mode != SacredDropCategoryMode.Uniform))
        {
            var button = new Button { Content = "Roll " + choice.Command };
            ToolTip.SetTip(button, choice.Description);
            button.Click += async (_, _) => { _category.SelectedItem = choice; await TryRollAsync(true); };
            _actions.Children.Add(button);
        }
        foreach (var button in _actions.Children) button.Margin = new Thickness(0, 0, 8, 8);
        var top = new StackPanel { Spacing = 10 };
        top.Children.Add(new TextBlock { Text = "Item drop simulator", FontSize = 24 });
        top.Children.Add(new TextBlock
        {
            Text = "Equipment drops with original category weights and rarity generation. Roll random rarity or choose a magic quality. Books and other unsupported item families are excluded; full source drop chances and sets are separate.",
            TextWrapping = TextWrapping.Wrap
        });
        top.Children.Add(inputs); top.Children.Add(_categoryInfo); top.Children.Add(_rarityContext); top.Children.Add(_actions);
        top.Children.Add(_authoredNames); top.Children.Add(_status);
        _rolls.ItemsSource = _history;
        _rolls.SelectionChanged += (_, _) => ShowSelected();
        var history = new DockPanel();
        var label = new TextBlock { Text = "Roll history · most recent first", Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(label, Dock.Top); history.Children.Add(label); history.Children.Add(_rolls);
        var body = new Grid { ColumnDefinitions = new("2*,3*"), ColumnSpacing = 18, Margin = new Thickness(0, 14, 0, 0) };
        body.Children.Add(history); Grid.SetColumn(_description, 1); body.Children.Add(_description);
        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(body); Content = root;
        Closed += (_, _) => { _closed = true; _description.Cancel(); };
        _loading = LoadAsync();
    }

    public Task Ready => Task.WhenAll(_loading, _description.Ready);
    public void CopyEquipmentFind() => _rarityContext.CopyEquipment();
    public void SaveScreenshot(string path) => _description.SaveScreenshot(path);

    public async Task RollAsync(SacredEquipmentDropRequest request)
    {
        await _loading;
        var result = (_simulator ?? throw new InvalidOperationException("Generation data is unavailable.")).Roll(request);
        if (_closed) return;
        _difficulty.SelectedIndex = (int)request.Difficulty; _level.Value = request.HeroLevel;
        _seed.Text = request.Seed.ToString(CultureInfo.InvariantCulture);
        _category.SelectedItem = ItemDropCategoryChoice.All.Single(choice => choice.Category == request.Category && choice.Mode == request.CategoryMode);
        _heroClass.SelectedItem = _heroClass.Items.Cast<ClassChoice>().Single(choice => choice.Code == request.HeroClass);
        _rarity.SelectedItem = ItemDropRarityChoice.All.Single(choice => choice.Quality == request.Quality);
        _rarityContext.Set(request.RarityContext);
        SacredItemInstance? instance = null;
        var name = result.SourceRarity is { RequiresSpecialResource: true } && request.RarityContext?.Profile == SacredLootRarityProfile.Creature
            ? "Special resource required" : result.UnavailableReason is null ? "No matching item" : "Category only";
        if (result.SelectedType.HasValue)
        {
            var valueType = BinaryPrimitives.ReadUInt32LittleEndian(result.Values.Span);
            var definition = _session.Data.GamePakStore.Weapons[result.SelectedType.Value];
            instance = SacredItemInstance.FromNativeObjectRecord(definition, result.SelectedType.Value, result.Values.Span);
            if (result.Name is { } generatedName) instance.SetResolvedName(generatedName);
            name = result.Name?.Text ?? _session.Data.GameResStore.GetString(valueType.ToString(CultureInfo.InvariantCulture), definition.Name);
        }
        var row = new ItemDropHistoryRow(++_number, result, instance, name);
        _history.Insert(0, row);
        if (_history.Count > 200) _history.RemoveAt(_history.Count - 1);
        _rolls.SelectedItem = row;
        Console.WriteLine($"[Drops] Roll #{row.Number}: difficulty {request.Difficulty}, hero level {request.HeroLevel}, category {result.Category}, mode {request.CategoryMode}, class {request.HeroClass}, seed {request.Seed}; selected {result.SelectedType?.ToString() ?? "none"}, item level {instance?.Level.ToString() ?? "none"}, final RNG {result.FinalRandomState}.");
        if (result.UnavailableReason is not null) Console.WriteLine($"[Drops] {result.UnavailableReason}");
        if (result.RolledQuality.HasValue) Console.WriteLine($"[Drops] Rarity: quality {result.RolledQuality}, final tier {result.RarityTier}.");
        if (result.Name is { } resolved) Console.WriteLine($"[Drops] Name: {resolved.Text}; native reference {resolved.NativeReference}; key {resolved.GeneratedKey ?? "authored"}.");
        if (request.RarityContext is { } context)
            Console.WriteLine($"[Drops] Rarity context: {context}; effective find {context.EffectiveFind(request.Difficulty)}; selector {result.SourceRarity?.Selector}.");
    }

    public void ClearDrops()
    {
        var count = _history.Count;
        _rolls.SelectedItem = null;
        _history.Clear(); _number = 0;
        _description.Clear("Roll an item, or select one from the history.", "Roll an item to preview.");
        _status.Text = "Drops cleared. Ready to roll with the current settings.";
        Console.WriteLine($"[Drops] Cleared {count} drops; history count {_history.Count}; selection none; preview cleared.");
    }

    public async Task InspectAuthoredAsync(uint type)
    {
        await _loading;
        var data = _generationData ?? throw new InvalidOperationException("Generation data is unavailable.");
        _authoredNames.Select(type);
        var name = new WorldEquipmentNames(data).Existing(type);
        var values = data.Templates.GetInstance(type);
        var definition = _session.Data.GamePakStore.Weapons[type];
        var instance = SacredItemInstance.FromNativeObjectRecord(definition, type, values);
        instance.SetResolvedName(name);
        _rolls.SelectedItem = null;
        _description.Show(instance);
        _status.Text = name.Text + " · original authored values";
        Console.WriteLine($"[Drops] Authored {type}: {name.Text}; values {Convert.ToHexString(values)}");
        _description.PrintDescription();
        await _description.Ready;
    }

    public async Task ReplayAsync()
    {
        var selected = _rolls.SelectedItem as ItemDropHistoryRow ?? throw new InvalidOperationException("Select a roll to replay.");
        await RollAsync(selected.Result.Request);
    }

    public void SelectHistory(int index)
    {
        if (index < 0 || index >= _history.Count) throw new ArgumentOutOfRangeException(nameof(index));
        _rolls.SelectedIndex = index;
    }

    public void PrintValues()
    {
        var selected = _rolls.SelectedItem as ItemDropHistoryRow ?? throw new InvalidOperationException("Select a roll first.");
        Console.WriteLine($"[Drops] {selected}; values {Convert.ToHexString(selected.Result.Values.Span)}");
        if (selected.Item is not null) _description.PrintDescription();
    }

    private async Task LoadAsync()
    {
        try
        {
            var data = await _session.LoadEquipmentGenerationAsync();
            if (_closed) return;
            _simulator = new(data); _actions.IsEnabled = true;
            _generationData = data; _authoredNames.Load(data);
            _status.Text = "Ready. Choose difficulty and hero level, then roll a new drop or a specific seed.";
        }
        catch (Exception error) { ShowError(error); throw; }
    }

    private async Task TryRollAsync(bool newSeed)
    {
        try
        {
            var value = _level.Value ?? throw new ArgumentException("Hero level is required.");
            if (decimal.Truncate(value) != value) throw new ArgumentException("Hero level must be a whole number.");
            var seed = newSeed ? (uint)Random.Shared.NextInt64(1L << 32) : uint.Parse(_seed.Text ?? "", CultureInfo.InvariantCulture);
            var choice = (ItemDropCategoryChoice)_category.SelectedItem!;
            await RollAsync(new(checked((ushort)value), checked((uint)_difficulty.SelectedIndex), seed,
                choice.Category, ((ClassChoice)_heroClass.SelectedItem!).Code, choice.Mode, ((ItemDropRarityChoice)_rarity.SelectedItem!).Quality)
                { RarityContext = _rarityContext.Read() });
        }
        catch (Exception error) { ShowError(error); }
    }

    private void ShowSelected()
    {
        if (_rolls.SelectedItem is not ItemDropHistoryRow row) return;
        if (row.Item is null)
        {
            var reason = row.Result.UnavailableReason ?? $"No {row.Result.Category} item matched the selected class preference and level.";
            _description.Clear(reason, "No item to preview.");
            _status.Text = reason;
        }
        else
        {
            _description.Show(row.Item);
            _status.Text = $"{row.Name} · hero level {row.Result.Request.HeroLevel}, item level {row.Item.Level} · quality {row.Result.RolledQuality}, rarity tier {row.Result.RarityTier} · seed {row.Result.Request.Seed}";
        }
    }

    private void ShowError(Exception error) { if (!_closed) _status.Text = error.Message; Console.WriteLine($"[Drops] Failed: {error}"); }
    private static Control Field(string label, Control input)
    {
        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 14, 8) };
        field.Children.Add(new TextBlock { Text = label }); field.Children.Add(input); return field;
    }
}
