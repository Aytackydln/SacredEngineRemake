using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Sacred.Core.Loot;
using Sacred.Inventory.Stats;

namespace AssetViewer.AssetViewer;

/// <summary>Explicit rarity-stage inputs; full loot-event parameters belong to source panes.</summary>
internal sealed class ItemDropRarityContextPane : StackPanel
{
    private readonly AssetViewerSession _session;
    private readonly ComboBox _profile = new() { MinWidth = 210, ItemsSource = new[] { "Neutral base weights", "Creature rarity stage", "Chest rarity stage" }, SelectedIndex = 0 };
    private readonly NumericUpDown _equipment = Number(-65535, 65535, 0, "0.##");
    private readonly NumericUpDown _survival = Number(0, uint.MaxValue, 0);
    private readonly NumericUpDown _sourceLevel = Number(1, ushort.MaxValue, 20);
    private readonly NumericUpDown _creatureBonus = Number(0, ushort.MaxValue, 0);
    private readonly NumericUpDown _unlocks = Number(0, 15, 0);
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap };

    public ItemDropRarityContextPane(AssetViewerSession session)
    {
        _session = session;
        Spacing = 5;
        var inputs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, control) in new (string, Control)[] { ("Rarity rules", _profile), ("Equipment find special items (%)", _equipment),
            ("Survival time (seconds)", _survival), ("Creature level", _sourceLevel), ("Creature resolved rarity bonus", _creatureBonus),
            ("Progression unlock mask (0–15)", _unlocks) })
        {
            var field = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 12, 5) };
            field.Children.Add(new TextBlock { Text = label }); field.Children.Add(control); inputs.Children.Add(field);
        }
        ToolTip.SetTip(_creatureBonus, "Resolved native word after source experience adjustment, capped at 700 for rarity. The upstream relation to equipment find is still being traced.");
        ToolTip.SetTip(_unlocks, "Four native progression bonuses, bits 0–3. Default zero; gameplay names remain unresolved.");
        var copy = new Button { Content = "Copy Characters equipment" };
        copy.Click += (_, _) =>
        {
            try
            {
                CopyEquipment();
            }
            catch (Exception error) { _info.Text = error.Message; Console.WriteLine($"[Drops] {error.Message}"); }
        };
        Children.Add(inputs); Children.Add(copy); Children.Add(_info);
        _profile.SelectionChanged += (_, _) => UpdateInfo();
        UpdateInfo();
    }

    public void CopyEquipment()
    {
        var actor = _session.DropHero ?? throw new InvalidOperationException("Select and equip a hero in Characters first.");
        _profile.SelectedIndex = 2;
        _equipment.Value = (decimal)SacredLootEquipmentStats.ForActor(actor);
        var count = actor.EquipmentSlots.Count(slot => slot.Instance is not null);
        Console.WriteLine($"[Drops] Copied Characters equipment: {count} references, find bonus {_equipment.Value}%. Chest rarity stage selected; level/class inputs remain as selected.");
        UpdateInfo();
    }

    public SacredLootRarityContext? Read()
    {
        if (_profile.SelectedIndex == 0) return null;
        foreach (var field in new[] { _survival, _sourceLevel, _creatureBonus, _unlocks })
            if (field.Value is { } value && decimal.Truncate(value) != value)
                throw new ArgumentException("Time, levels, caller bonus and unlock mask must be whole numbers.");
        return new(_profile.SelectedIndex == 1 ? SacredLootRarityProfile.Creature : SacredLootRarityProfile.Container,
            (float)(_equipment.Value ?? 0), checked((uint)(_survival.Value ?? 0)), checked((ushort)(_sourceLevel.Value ?? 1)),
            checked((ushort)(_creatureBonus.Value ?? 0)), checked((byte)(_unlocks.Value ?? 0)));
    }

    public void Set(SacredLootRarityContext? context)
    {
        _profile.SelectedIndex = context is null ? 0 : context.Value.Profile == SacredLootRarityProfile.Creature ? 1 : 2;
        _equipment.Value = (decimal)(context?.EquipmentFindBonus ?? 0); _survival.Value = context?.SurvivalSeconds ?? 0;
        _sourceLevel.Value = context?.SourceLevel ?? 20; _creatureBonus.Value = context?.CreatureBonus ?? 0; _unlocks.Value = context?.UnlockMask ?? 0;
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        _equipment.IsEnabled = _survival.IsEnabled = _unlocks.IsEnabled = _profile.SelectedIndex == 2;
        _sourceLevel.IsEnabled = _creatureBonus.IsEnabled = _profile.SelectedIndex == 1;
        _info.Text = _profile.SelectedIndex switch
        {
            1 => "Creature rarity stage only: includes level penalty and caller bonus. Full kill chances/counts and special-resource selection are still pending.",
            2 => "Chest rarity stage only: includes equipment find and survival time. Full opening chances, budgets and source level resolution are still pending.",
            _ => "Neutral rarity exploration. Fixed quality overrides rarity rules. Complete monster, chest and quest event modes are under development."
        };
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value, string format = "0") =>
        new() { Minimum = min, Maximum = max, Value = value, Increment = 1, FormatString = format, MinWidth = 105 };
}
