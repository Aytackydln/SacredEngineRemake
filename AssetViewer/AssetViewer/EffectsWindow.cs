using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal sealed class EffectsWindow : Window, IAssetViewerWindow
{
    private readonly AssetViewerSession _session;
    private readonly SacredParticleCatalogue _catalogue = SacredParticleCatalogue.LoadEmbedded();
    private readonly AssetTableControl<EffectAssetRow> _table;
    private readonly ModelPreviewPane _preview;
    private readonly ComboBox _variants = new() { MinWidth = 260 };
    private readonly CheckBox _implemented = new() { Content = "Implemented", Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _randomActor = new() { Content = "Random character", IsEnabled = false };
    private readonly Button _playPause = new() { Content = "Pause" };
    private readonly TextBlock _actorLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly NumericUpDown _strength = new() { Minimum = 0, Maximum = uint.MaxValue, Increment = 100, Value = 0, Width = 130 };
    private readonly StackPanel _strengthControls = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown _distance = new() { Minimum = 0, Maximum = (decimal)FxPreviewParameters.MaximumDistance,
        Increment = 10, Width = 150, PlaceholderText = "Model endpoints" };
    private readonly StackPanel _distanceControls = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas") };
    private readonly ItemsPakEntry[] _actors;
    private ItemsPakEntry? _actor;
    private SacredEquipment? _weapon;
    private CancellationTokenSource? _selectionCancellation;
    private Task _selection = Task.CompletedTask;
    private bool _updating;

    public EffectsWindow(AssetViewerSession session)
    {
        _session = session;
        Title = "Sacred Asset Viewer · Effects - Items.pak";
        Width = 1450; Height = 900;
        var rows = session.Items.Where(item => item.ModelDesc.Category == SacredItemCategory.Effect)
            .Select(item => new EffectAssetRow(item,
                _catalogue.TryGetDefinition(item.ItemIndex, out var definition) ? definition : null)
            {
                EventVariants = _catalogue.EventDefinitions.Count(definition =>
                    definition.TypeId == item.ItemIndex)
            }).ToArray();
        var namedFamilies = _catalogue.Definitions.Concat(_catalogue.EventDefinitions)
            .Where(definition => definition.NativeConstructorAddress is not null && definition.NativeClass is not null)
            .GroupBy(definition => definition.NativeConstructorAddress!.Value)
            .ToDictionary(group => group.Key, group => group.First().NativeClass!);
        rows = rows.Select(row => new EffectAssetRow(row.Item, row.Definition)
        {
            EventVariants = row.EventVariants,
            Family = row.Definition?.NativeConstructorAddress is { } constructor
                ? namedFamilies.GetValueOrDefault(constructor, $"Constructor 0x{constructor:X8}") : "Unresolved",
            FamilyCoverage = row.Definition?.NativeConstructorAddress is { } address
                ? $"{rows.Count(member => member.Definition?.NativeConstructorAddress == address && member.Implemented)}/{rows.Count(member => member.Definition?.NativeConstructorAddress == address)} rows"
                : "Unresolved"
        }).ToArray();
        _table = new(rows, row => $"{row.EntryId} {row.Effect} {row.NativeName} {row.Status} {row.Family}");
        _actors = session.Items.Where(item => item.ModelDesc.Category == SacredItemCategory.Creature &&
            !string.IsNullOrWhiteSpace(item.ModelName) && session.Data.Creatures?.ResolveTemplate(item)?.PlayableClassMask
                is { } mask && mask != SacredCharacterClassMask.None).ToArray();
        _preview = new(session);
        _preview.SetGroundGridEnabled(true);
        var restart = new Button { Content = "Restart" };
        restart.Click += (_, _) => Restart();
        _playPause.Click += (_, _) => SetPlaying(!(_preview.FxPlayback?.Playing ?? true));
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(_variants); controls.Children.Add(restart);
        controls.Children.Add(_playPause);
        _strengthControls.Children.Add(new TextBlock { Text = "Strength", VerticalAlignment = VerticalAlignment.Center });
        _strengthControls.Children.Add(_strength);
        _distanceControls.Children.Add(new TextBlock { Text = "Distance", VerticalAlignment = VerticalAlignment.Center });
        _distanceControls.Children.Add(_distance);
        var defaults = new Button { Content = "Default inputs" };
        defaults.Click += (_, _) => ResetInputs();
        controls.Children.Add(_distanceControls); controls.Children.Add(defaults);
        foreach (var control in controls.Children) control.Margin = new Thickness(0, 0, 8, 8);
        var actorControls = new WrapPanel { Orientation = Orientation.Horizontal };
        actorControls.Children.Add(_randomActor); actorControls.Children.Add(_actorLabel);
        actorControls.Children.Add(_strengthControls);
        foreach (var control in actorControls.Children) control.Margin = new Thickness(0, 0, 8, 8);
        var toolbar = new StackPanel(); toolbar.Children.Add(controls); toolbar.Children.Add(actorControls);
        var right = new Grid { RowDefinitions = new("Auto,3*,2*") };
        right.Children.Add(toolbar);
        Grid.SetRow(_preview, 1); right.Children.Add(_preview);
        Grid.SetRow(_details, 2); right.Children.Add(_details);
        var root = new Grid { ColumnDefinitions = new("3*,5,4*"), Margin = new Thickness(12) };
        var browser = new DockPanel();
        DockPanel.SetDock(_implemented, Dock.Top);
        browser.Children.Add(_implemented); browser.Children.Add(_table);
        root.Children.Add(browser);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1); root.Children.Add(splitter);
        Grid.SetColumn(right, 2); root.Children.Add(right); Content = root;
        _table.SelectedChanged += SelectRow;
        _implemented.IsCheckedChanged += (_, _) => ApplyImplementedFilter();
        _variants.SelectionChanged += (_, _) => { if (!_updating) Rebuild(); };
        _randomActor.Click += (_, _) => RandomizeActor();
        _strength.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            var value = _strength.Value ?? 0;
            if (value != decimal.Truncate(value)) { _strength.Value = decimal.Truncate(value); return; }
            Rebuild();
        };
        _distance.ValueChanged += (_, _) => { if (!_updating) Rebuild(); };
        Closed += (_, _) => { _selectionCancellation?.Cancel(); _preview.Cancel(); };
        Console.WriteLine($"[Assets] Effects catalog: {rows.Length} Items.pak rows; {rows.Count(row => row.Definition?.Status == SacredParticleDefinitionStatus.Decoded)} decoded script effects; {rows.Sum(row => row.EventVariants)} event variants; {rows.Count(row => row.Portal is not null)} portal meshes; {_actors.Length} playable actor templates.");
    }

    public Task Ready => _selection.IsCompletedSuccessfully ? _preview.CurrentLoad : _selection;
    public void ZoomBy(double delta) => _preview.ZoomBy(delta);
    public void SetImplementedFilter(bool enabled) => _implemented.IsChecked = enabled;
    public void SetSearchText(string text)
    {
        _table.SetSearchText(text);
        Console.WriteLine($"[Assets] FX search: {text}; {_table.FilteredCount} matching rows.");
    }

    private void ApplyImplementedFilter()
    {
        var enabled = _implemented.IsChecked == true;
        _table.SetRowFilter(enabled ? row => row.Implemented : null);
        Console.WriteLine($"[Assets] FX Implemented filter: {enabled}; {_table.FilteredCount} matching rows.");
    }
    public void Select(ushort id) => _table.Select(row => row.EntryId == id);
    public void SaveScreenshot(string path) => _preview.SaveScreenshot(path);
    public void RotateHorizontally(float radians) => _preview.RotateHorizontally(radians);
    public void Restart() { _preview.FxPlayback?.Restart(); SetPlaying(true); Console.WriteLine("[Assets] FX restarted."); }
    public void SetPlaying(bool playing)
    {
        if (_preview.FxPlayback is { } playback) playback.Playing = playing;
        _playPause.Content = playing ? "Pause" : "Play";
        _preview.SetAnimationPlaying(playing);
        Console.WriteLine($"[Assets] FX {(playing ? "playing" : "paused")}.");
    }
    public void SetTime(float seconds)
    {
        (_preview.FxPlayback ?? throw new InvalidOperationException("Select a supported FX first.")).SetTime(seconds);
        SetPlaying(false);
        _preview.RefreshFxFraming();
        Console.WriteLine($"[Assets] FX time: {seconds:F3}s.");
    }
    public void RandomizeActor() { _actor = null; _weapon = null; Rebuild(); }
    public void SetStrength(uint strength)
    {
        if ((_variants.SelectedItem as EffectVariantChoice)?.Definition.Strength is null)
            throw new InvalidOperationException("This FX has no native strength input.");
        _strength.Value = strength;
        Console.WriteLine($"[Assets] FX strength: {strength}.");
    }
    public void SetDistance(float distance)
    {
        if ((_variants.SelectedItem as EffectVariantChoice)?.Definition is not { } definition ||
            !FxPreviewParameters.UsesDistance(definition)) throw new InvalidOperationException("This FX has no preview distance input.");
        new FxPreviewParameters(distance).Validate();
        _distance.Value = (decimal)distance;
        Console.WriteLine($"[Assets] FX distance: {distance} native units.");
    }
    public void ResetInputs()
    {
        _updating = true;
        _strength.Value = 0;
        _distance.Value = (_variants.SelectedItem as EffectVariantChoice)?.Definition is { } definition &&
            FxPreviewParameters.Default(definition).Distance is { } distance ? (decimal)distance : null;
        _updating = false;
        Rebuild();
        Console.WriteLine("[Assets] FX inputs reset to showcase defaults.");
    }
    public void SelectVariant(int? preset)
    {
        _variants.SelectedItem = _variants.Items.OfType<EffectVariantChoice>().FirstOrDefault(choice =>
            preset is { } value ? choice.Definition.IsEventPreset && choice.Definition.Preset == value : !choice.Definition.IsEventPreset)
            ?? throw new ArgumentException("That native event variant is unavailable for this FX row.");
    }

    private void SelectRow(EffectAssetRow row)
    {
        _updating = true;
        _variants.IsEnabled = row.Portal is null;
        var choices = (row.Definition is { } definition ? new[] { definition } : [])
            .Concat(_catalogue.EventDefinitions.Where(definition => definition.TypeId == row.EntryId))
            .OrderBy(definition => definition.IsEventPreset).ThenBy(definition => definition.Preset)
            .Select(definition => new EffectVariantChoice(definition)).ToArray();
        _variants.ItemsSource = choices;
        _variants.SelectedItem = choices.FirstOrDefault(choice => choice.Definition.Status == SacredParticleDefinitionStatus.Decoded)
            ?? choices.FirstOrDefault();
        _strength.Value = 0;
        _distance.Value = (_variants.SelectedItem as EffectVariantChoice)?.Definition is { } selected &&
            FxPreviewParameters.Default(selected).Distance is { } distance ? (decimal)distance : null;
        _updating = false;
        _actor = null;
        _weapon = null;
        Rebuild();
    }

    private void Rebuild()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = new();
        _selection = Task.CompletedTask;
        if (_table.Selected is not { } row) return;
        SetPlaying(true);
        var definition = (_variants.SelectedItem as EffectVariantChoice)?.Definition;
        _strengthControls.IsVisible = definition?.Strength is not null;
        _distanceControls.IsVisible = definition is not null && FxPreviewParameters.UsesDistance(definition);
        var parameters = new FxPreviewParameters(_distance.Value is { } distance ? (float)distance : null);
        var strength = checked((uint)(_strength.Value ?? 0));
        var text = new StringBuilder();
        text.AppendLine($"Effect: {row.Effect}; native type: {definition?.TypeName ?? "unmapped"}");
        text.AppendLine($"Family: {row.Family}; preview coverage: {row.FamilyCoverage}");
        text.AppendLine($"Status: {definition?.Status.ToString() ?? "Unmapped"}; family: {definition?.NativeClass}; preset: {definition?.Preset}");
        text.AppendLine($"{definition?.Diagnostic}");
        text.AppendLine($"Actor seed limit: {definition?.ModelBurstCount ?? 0}; finite burst: {definition?.OneTime ?? false}");
        var attachment = definition is null ? null : SacredModelFxAttachmentCatalogue.Find(definition);
        _randomActor.Content = attachment is null ? "Random character" : "Random weapon";
        if (attachment is not null)
            text.AppendLine($"Weapon GRN attachment: {attachment.StartBoneName}" +
                (attachment.EndBoneName is { } end ? $" through {end}" : string.Empty) + "; authored rest pose.");
        else if (definition is not null && FxPreviewParameters.UsesPointSegment(definition))
            text.AppendLine("The random character marks the first preview endpoint.");
        else if (definition?.RequiresActorContext == true)
            text.AppendLine(definition.LineEmission is null
                ? "Actor attachment preview uses the character origin; individual bone attachment is pending."
                : "Line preview inputs span the actor bounds; individual bone attachment is pending.");
        if (definition?.Strength is not null) text.AppendLine($"Native event strength: {strength}");
        if (definition is not null && FxPreviewParameters.UsesDistance(definition))
        {
            text.AppendLine(parameters.Distance is { } length ? $"Preview distance: {length} native units."
                : "Preview distance: authored weapon endpoints or actor mesh bounds.");
            if (FxPreviewParameters.UsesPointSegment(definition))
                text.AppendLine("Two-point showcase: five independent native samples along the segment; game placement scheduling remains pending.");
        }
        text.AppendLine("Native event parameters come from the recovered effect catalog; the descriptor is shown below.");
        text.AppendLine(ModelsWindow.Describe(row.Item));
        _details.Text = text.ToString();
        _randomActor.IsEnabled = definition?.RequiresActor == true;
        _actorLabel.Text = string.Empty;
        if (row.Portal is { } portal)
        {
            _variants.IsEnabled = false;
            _randomActor.IsEnabled = false;
            _actorLabel.Text = $"{portal} portal mesh";
            _details.Text = $"Effect: {row.Effect}; visual recipe: {portal} portal mesh\n" +
                $"Family: {row.Family}; preview coverage: {row.FamilyCoverage}\n" +
                "Native 20x34 surface, vertex colours, rotating whirls and five-second minimap transitions.\n\n" + ModelsWindow.Describe(row.Item);
            Console.WriteLine($"[Assets] FX selected: {row.EntryId} {row.Effect}; portal={portal}; actor=none.");
            _ = _preview.LoadAsync(row.Effect,
                _ => Task.FromResult(new GrnAsset(row.Effect, [], null, new Mesh([], []))), [],
                fxFactory: _ => new FxPreviewPlayback(_catalogue, portal));
            return;
        }
        if (definition?.Status != SacredParticleDefinitionStatus.Decoded || definition.Draw is null)
        {
            _preview.ShowStatus($"{row.Effect}: {definition?.Diagnostic ?? "Native recipe has not been mapped yet."}");
            Console.WriteLine($"[Assets] FX unavailable: {row.EntryId} {row.Effect}: {definition?.Status.ToString() ?? "Unmapped"}.");
            return;
        }
        if (attachment is not null)
        {
            _randomActor.IsEnabled = true;
            _selection = LoadWeaponAsync(row, definition, attachment, strength, parameters, _selectionCancellation.Token);
            return;
        }
        if (definition.RequiresActor)
        {
            if (_actors.Length == 0) { _preview.ShowStatus("This effect needs an actor; no playable Creature.pak template is available."); return; }
            _actor ??= _actors[Random.Shared.Next(_actors.Length)];
        }
        else _actor = null;
        var actor = _actor;
        if (definition.UsesActorBlockRadius && actor is { } scaledActor)
            _details.Text = $"Actor Items.pak row {scaledActor.ItemIndex}: BlockRadius={scaledActor.ModelDesc.BlockRadius}; " +
                "the native zero-field fallback is 50.\n\n" + _details.Text;
        _actorLabel.Text = actor is null ? "Standalone effect" : $"{actor.Value.ModelName} ({actor.Value.ItemIndex})";
        Console.WriteLine($"[Assets] FX selected: {row.EntryId} {row.Effect}; event={definition.IsEventPreset}; preset={definition.Preset}; actor={actor?.ModelName ?? "none"}.");
        _ = _preview.LoadAsync(row.Effect,
            token => actor is null ? Task.FromResult(new GrnAsset(row.Effect, [], null, new Mesh([], [])))
                : _session.Models.LoadCharacterBaseModelAsync(actor.Value.ModelName, [], token),
            actor is null ? [] : [new ModelPreviewVisual(actor.Value)], fxFactory: asset =>
                new FxPreviewPlayback(_catalogue, definition, actor is null ? null : asset, actor?.ModelDesc.BlockRadius,
                    strength, parameters: parameters));
    }

    private async Task LoadWeaponAsync(EffectAssetRow row, SacredParticleDefinition definition,
        SacredModelFxAttachmentDefinition attachment, uint strength, FxPreviewParameters parameters, CancellationToken token)
    {
        _preview.ShowStatus($"{row.Effect}: loading a weapon with {attachment.StartBoneName}...");
        try
        {
            var selected = await FxPreviewEquipmentLoader.LoadAsync(_session.Models, _session.Equipment,
                _catalogue, definition, attachment, _weapon, token);
            token.ThrowIfCancellationRequested();
            if (selected is null)
            {
                _actorLabel.Text = "No compatible weapon";
                _preview.ShowStatus("No weapon model contains the native FX attachment helpers.");
                return;
            }
            _weapon = selected.Equipment;
            _actor = null;
            var item = selected.Equipment.Item;
            _actorLabel.Text = $"{item.ModelName} ({item.ItemIndex})";
            _details.Text = $"Weapon: {selected.Equipment.Name}; Items.pak row {item.ItemIndex}; " +
                $"BaseItemId={selected.Equipment.BaseItemId}; " +
                (selected.Related ? "native equipment selector matched.\n\n" : "compatible GRN helpers; preview event applied.\n\n") + _details.Text;
            Console.WriteLine($"[Assets] FX selected: {row.EntryId} {row.Effect}; event={definition.IsEventPreset}; preset={definition.Preset}; weapon={item.ModelName}; related={selected.Related}; attachment={attachment.StartBoneName}; end={attachment.EndBoneName ?? "none"}.");
            await _preview.LoadAsync(row.Effect, _ => Task.FromResult(selected.Asset),
                [new ModelPreviewVisual(item, selected.Equipment)], fxFactory: asset =>
                    new FxPreviewPlayback(_catalogue, definition, asset, item.ModelDesc.BlockRadius, strength,
                        selected.Inputs, parameters));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
