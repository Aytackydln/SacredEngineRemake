using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Bonuses;
using Sacred.Core.Pak.Weapon.Descriptions;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Stats;
using Sacred.UI.Character;
using static Sacred.UI.Character.CharacterStatLabels;

namespace AssetViewer.AssetViewer;

internal sealed class CharacterStatsPane : UserControl
{
    private readonly GameResStore _resources;
    private readonly CharacterStatLabels _labels;
    private readonly TextBlock _identity = Text();
    private readonly TextBlock _attributes = Text();
    private readonly TextBlock _attack = Text();
    private readonly TextBlock _protection = Text();
    private readonly TextBlock _movement = Text();
    private readonly TextBlock _skills = Text();
    private readonly StackPanel _skillListing = new() { Spacing = 8 };
    private readonly StackPanel _artListing = new() { Spacing = 6 };
    private readonly TextBlock _bonuses = Text();
    private readonly CharacterAllocationPane _allocation;
    private readonly TabControl _tabs;
    private SacredGameActor? _actor;
    private CharacterCombatArtPresentation? _artPresentation;

    public void ConfigureArtPresentation(CharacterCombatArtPresentation presentation)
    {
        _artPresentation = presentation;
        _allocation.ArtPresentation = presentation;
    }

    public CharacterStatsPane(GameResStore resources)
    {
        _resources = resources;
        _labels = new(resources);
        _allocation = new(_labels);
        var stats = new StackPanel { Spacing = 12 };
        stats.Children.Add(Heading("Character + equipment = total")); stats.Children.Add(_attributes);
        stats.Children.Add(new TextBlock { Text = "Direct equipment combat values", TextWrapping = TextWrapping.Wrap });
        stats.Children.Add(Heading("Attack")); stats.Children.Add(_attack);
        stats.Children.Add(Heading("Protection")); stats.Children.Add(_protection);
        stats.Children.Add(_movement);
        _skillListing.Children.Add(_skills);
        _skillListing.Children.Add(Heading("Acquired combat arts · base + bonus = total"));
        _skillListing.Children.Add(_artListing);
        _tabs = new TabControl { ItemsSource = new[] { Tab("Stats", stats), Tab("Skills", _skillListing), Tab("Bonuses", _bonuses), Tab("Allocate", _allocation) } };
        var root = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(_identity, Dock.Top); root.Children.Add(_identity); root.Children.Add(_tabs);
        Content = root;
    }

    public void SelectActor(SacredGameActor actor)
    {
        if (_actor is { } previous) previous.StatsChanged -= Refresh;
        _actor = actor;
        actor.StatsChanged += Refresh;
        _allocation.SelectActor(actor);
        Refresh();
    }

    public void PrintStats()
    {
        Refresh();
        Console.WriteLine($"[Stats] {_identity.Text}\n{_attributes.Text}\nAttack:\n{_attack.Text}\nProtection:\n{_protection.Text}\n{_movement.Text}\nSkills:\n{_skills.Text}\nBonuses:\n{_bonuses.Text}");
        if (_actor is { } actor)
            foreach (var art in actor.TotalStats.CombatArts.OrderBy(entry => entry.Key))
            {
                Console.WriteLine($"[Stats] Art {art.Key}: {_labels.Art(art.Key)}; {actor.InherentStats.CombatArts.GetValueOrDefault(art.Key)} + {actor.EquipmentStats.CombatArts.GetValueOrDefault(art.Key)} = {art.Value}");
                _artPresentation?.Print(art.Key, _labels.Art(art.Key));
            }
    }
    public void PrintAllocation() => _allocation.PrintAllocation();
    public void SelectView(string name) => _tabs.SelectedIndex = name.ToLowerInvariant() switch
    {
        "stats" => 0, "skills" => 1, "bonuses" => 2, "allocate" => 3,
        _ => throw new ArgumentException("Use stats, skills, bonuses or allocate.", nameof(name))
    };

    private void Refresh()
    {
        if (_actor is not { } actor) return;
        var template = actor.Progression.Template;
        _identity.Text = template is null ? "No Creature.pak template mapped to this model." :
            $"{template.Class} · Level {actor.Progression.Level} · Template {template.RecordIndex}\n";
        _attributes.Text = string.Join('\n', Enum.GetValues<SacredActorStat>().Take(6).Select(stat =>
            $"{_labels.Attribute(stat)}\n  {Number(actor.InherentStats[stat])} + {Number(actor.EquipmentStats[stat])} = {Number(actor.TotalStats[stat])}"));
        var total = actor.TotalStats;
        _attack.Text = $"Attack rating: {Number(total[SacredActorStat.Attack])}\nAttack speed bonus: {Number(total[SacredActorStat.AttackSpeed])}\n" +
            string.Join('\n', Enumerable.Range(0, 4).Select(element =>
                $"{Element(element)} damage: {Number(total[SacredActorStat.PhysicalDamageMinimum + element])}–{Number(total[SacredActorStat.PhysicalDamageMaximum + element])}"));
        _protection.Text = $"Defense rating: {Number(total[SacredActorStat.Defense])}\n" +
            string.Join('\n', Enumerable.Range(0, 4).Select(element => $"{Element(element)} protection: {Number(total[SacredActorStat.PhysicalProtection + element])}"));
        _movement.Text = $"Run speed (raw): {Number(total[SacredActorStat.MovementSpeed])}";
        _skills.Text = "Acquired skills · base + bonus = total\n" + string.Join('\n', total.Skills.OrderBy(entry => entry.Key).Select(entry =>
            $"{_labels.Skill(entry.Key)}: {actor.InherentStats.Skills.GetValueOrDefault(entry.Key)} + {actor.EquipmentStats.Skills.GetValueOrDefault(entry.Key)} = {entry.Value}"));
        _artListing.Children.Clear();
        if (total.CombatArts.Count == 0) _artListing.Children.Add(TextWith("No combat arts acquired."));
        foreach (var art in total.CombatArts.OrderBy(entry => entry.Key))
        {
            var name = $"{_labels.Art(art.Key)}: {actor.InherentStats.CombatArts.GetValueOrDefault(art.Key)} + {actor.EquipmentStats.CombatArts.GetValueOrDefault(art.Key)} = {art.Value}";
            _artListing.Children.Add(_artPresentation?.Create(art.Key, name) ?? TextWith(name));
        }
        _bonuses.Text = string.Join('\n', actor.BonusSummary.Select(totalBonus =>
        {
            var field = SacredEquipmentBonusDescriptions.CreateTotal(totalBonus.Bonus, totalBonus.Value, _resources);
            return field is null ? $"Unknown bonus {totalBonus.Bonus.Code}: {totalBonus.Value}" : $"{field.Label}: {field.Value}";
        }));
        if (actor.BonusSummary.Count == 0) _bonuses.Text = "No equipped bonuses.";
    }

    private string Element(int element) => _labels.Text(1078 + element, ((SacredEquipmentElement)element).ToString());
    private static TextBlock Text() => new() { TextWrapping = TextWrapping.Wrap };
    private static TextBlock TextWith(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.Bold };
    private static TabItem Tab(string name, Control content) => new() { Header = name, Content = new ScrollViewer { Content = content, Margin = new Thickness(8) } };
}
