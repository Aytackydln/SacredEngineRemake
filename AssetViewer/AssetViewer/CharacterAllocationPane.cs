using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Stats;

namespace AssetViewer.AssetViewer;

internal sealed class CharacterAllocationPane : UserControl
{
    private readonly CharacterStatLabels _labels;
    private readonly NumericUpDown _level = new() { Minimum = 1, Maximum = ushort.MaxValue, Value = 1, Width = 110, ShowButtonSpinner = false, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<SacredActorStat, AttributeRow> _attributes = new();
    private readonly StackPanel _skills = new() { Spacing = 6 };
    private readonly StackPanel _arts = new() { Spacing = 6 };
    private readonly Dictionary<int, CharacterRankRow> _skillRows = new();
    private readonly Dictionary<int, CharacterRankRow> _artRows = new();
    private readonly ComboBox _newSkill = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _newArt = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _addSkill = new() { Content = "Add skill" };
    private readonly Button _addArt = new() { Content = "Add art" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private SacredGameActor? _actor;
    private bool _updating;
    public CharacterCombatArtPresentation? ArtPresentation { get; set; }

    public CharacterAllocationPane(CharacterStatLabels labels)
    {
        _labels = labels;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Heading("Character level")); panel.Children.Add(_level);
        panel.Children.Add(Heading("Attributes"));
        panel.Children.Add(Header("Attribute", "Base", "Added", "Bonus", "Total"));
        foreach (var attribute in Enum.GetValues<SacredActorStat>().Take(6))
        {
            var row = new AttributeRow();
            _attributes.Add(attribute, row);
            var grid = new Grid { ColumnDefinitions = new("*,55,70,50,50"), ColumnSpacing = 4 };
            grid.Children.Add(new TextBlock { Text = labels.Attribute(attribute), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            Add(grid, row.Base, 1); Add(grid, row.Points, 2); Add(grid, row.Bonus, 3); Add(grid, row.Total, 4);
            panel.Children.Add(grid);
            row.Points.ValueChanged += (_, _) =>
            {
                if (!_updating && _actor is { } actor)
                    Apply(() => actor.Progression.SetAllocatedAttribute(attribute, (int)(row.Points.Value ?? 0)));
            };
        }
        panel.Children.Add(new TextBlock { Text = "Base includes level growth. Added points and acquired levels are editable.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Heading("Acquired skills")); panel.Children.Add(Header("Skill", "Base", "Bonus", "Total", ""));
        panel.Children.Add(_skills); panel.Children.Add(Chooser(_newSkill, _addSkill));
        panel.Children.Add(Heading("Acquired combat arts")); panel.Children.Add(Header("Combat art", "Base", "Bonus", "Total", ""));
        panel.Children.Add(_arts); panel.Children.Add(Chooser(_newArt, _addArt)); panel.Children.Add(_status);
        Content = panel;
        _level.ValueChanged += (_, _) => { if (!_updating && _actor is { } actor) Apply(() => actor.Progression.SetLevel((ushort)(_level.Value ?? 1))); };
        _addSkill.Click += (_, _) => { if (_actor is { } actor && _newSkill.SelectedItem is AllocationChoice choice) Apply(() => actor.Progression.SetSkill((byte)choice.Id, 1)); };
        _addArt.Click += (_, _) => { if (_actor is { } actor && _newArt.SelectedItem is AllocationChoice choice) Apply(() => actor.Progression.SetCombatArt((ushort)choice.Id, 1)); };
        _newArt.ItemTemplate = new FuncDataTemplate<AllocationChoice>((choice, _) => choice is null ? new TextBlock() :
            ArtPresentation?.Create((ushort)choice.Id, choice.Name) ?? new TextBlock { Text = choice.Name });
    }

    public void SelectActor(SacredGameActor actor)
    {
        if (_actor is { } previous) previous.StatsChanged -= Refresh;
        _actor = actor;
        _skillRows.Clear(); _artRows.Clear(); _skills.Children.Clear(); _arts.Children.Clear();
        _status.Text = null;
        actor.StatsChanged += Refresh;
        Refresh();
    }

    public void PrintAllocation()
    {
        if (_actor is not { } actor) return;
        Console.WriteLine($"[Allocation] Level {actor.Progression.Level}; visible attributes {_attributes.Count}; acquired skills {actor.InherentStats.Skills.Count}; acquired arts {actor.InherentStats.CombatArts.Count}.");
        Console.WriteLine("[Allocation] Available skills: " + string.Join(", ", Choices(_newSkill).Select(choice => $"{choice.Id}: {choice.Name}")));
        Console.WriteLine("[Allocation] Available combat arts: " + string.Join(", ", Choices(_newArt).Select(choice => $"{choice.Id}: {choice.Name}")));
        foreach (var code in actor.InherentStats.CombatArts.Keys) ArtPresentation?.Print(code, _labels.Art(code));
    }

    private void Refresh()
    {
        if (_actor is not { } actor) return;
        _updating = true;
        _level.Value = actor.Progression.Level;
        foreach (var (stat, row) in _attributes)
        {
            var added = actor.Progression.GetAllocatedAttribute(stat);
            row.Base.Text = CharacterStatLabels.Number(unchecked((ushort)(int)(actor.InherentStats[stat] - added)));
            row.Points.Value = added;
            row.Bonus.Text = CharacterStatLabels.Number(actor.EquipmentStats[stat]);
            row.Total.Text = CharacterStatLabels.Number(actor.TotalStats[stat]);
        }
        UpdateRanks(_skills, _skillRows, actor.InherentStats.Skills.ToDictionary(entry => (int)entry.Key, entry => entry.Value),
            id => _labels.Skill((byte)id), id => actor.EquipmentStats.Skills.GetValueOrDefault((byte)id),
            id => actor.TotalStats.Skills.GetValueOrDefault((byte)id), (id, rank) => actor.Progression.SetSkill((byte)id, rank));
        UpdateRanks(_arts, _artRows, actor.InherentStats.CombatArts.ToDictionary(entry => (int)entry.Key, entry => entry.Value),
            id => _labels.Art((ushort)id), id => actor.EquipmentStats.CombatArts.GetValueOrDefault((ushort)id),
            id => actor.TotalStats.CombatArts.GetValueOrDefault((ushort)id), (id, rank) => actor.Progression.SetCombatArt((ushort)id, rank));
        var template = actor.Progression.Template;
        UpdateChoices(_newSkill, template is null ? [] : template.StartingSkills.Concat(template.AvailableSkills)
            .Where(id => id != 0 && !actor.InherentStats.Skills.ContainsKey(id)).Distinct()
            .Select(id => new AllocationChoice(id, _labels.Skill(id))).ToArray());
        UpdateChoices(_newArt, actor.Progression.AvailableCombatArts.Where(art => !actor.InherentStats.CombatArts.ContainsKey(art.Code))
            .Select(art => new AllocationChoice(art.Code, _labels.Art(art.Code))).ToArray());
        _addSkill.IsEnabled = Choices(_newSkill).Length > 0 && actor.InherentStats.Skills.Count < 8;
        _addArt.IsEnabled = Choices(_newArt).Length > 0;
        _updating = false;
    }

    private void UpdateRanks(StackPanel panel, Dictionary<int, CharacterRankRow> rows, Dictionary<int, int> ranks,
        Func<int, string> name, Func<int, int> bonus, Func<int, int> total, Action<int, int> setRank)
    {
        foreach (var id in rows.Keys.Except(ranks.Keys).ToArray()) { panel.Children.Remove(rows[id]); rows.Remove(id); }
        foreach (var (id, rank) in ranks.OrderBy(entry => entry.Key))
        {
            if (!rows.TryGetValue(id, out var row))
            {
                row = new(name(id), value => Apply(() => setRank(id, value)));
                if (panel == _arts && ArtPresentation is { } presentation) row.NameContent = presentation.Create((ushort)id, name(id));
                rows.Add(id, row); panel.Children.Add(row);
            }
            row.Update(rank, bonus(id), total(id));
        }
    }

    private void Apply(Action change)
    {
        try { change(); _status.Text = null; Console.WriteLine("[Assets] Allocation updated."); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _status.Text = error.Message; Console.WriteLine($"[Assets] Allocation rejected: {error.Message}"); Refresh();
        }
    }

    private static AllocationChoice[] Choices(ComboBox combo) => combo.ItemsSource as AllocationChoice[] ?? [];
    private static void UpdateChoices(ComboBox combo, AllocationChoice[] choices)
    {
        if (Choices(combo).SequenceEqual(choices)) return;
        var selected = combo.SelectedItem as AllocationChoice;
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected?.Id) ?? choices.FirstOrDefault();
    }
    private static Grid Chooser(ComboBox combo, Button button)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        grid.Children.Add(combo); Add(grid, button, 1); return grid;
    }
    private static Grid Header(params string[] names)
    {
        var grid = new Grid { ColumnDefinitions = new(names[0] == "Attribute" ? "*,55,70,50,50" : "*,70,50,50,30"), ColumnSpacing = 4 };
        for (var i = 0; i < names.Length; i++) Add(grid, Heading(names[i]), i);
        return grid;
    }
    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.Bold };
    private static void Add(Grid grid, Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
    private sealed record AllocationChoice(int Id, string Name) { public override string ToString() => Name; }
    private sealed class AttributeRow
    {
        public TextBlock Base { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
        public NumericUpDown Points { get; } = new() { Minimum = 0, Maximum = byte.MaxValue, Value = 0, ShowButtonSpinner = false, FormatString = "0" };
        public TextBlock Bonus { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
        public TextBlock Total { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
    }
}
