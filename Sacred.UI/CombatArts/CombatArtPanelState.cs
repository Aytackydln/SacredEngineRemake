using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Assets.Executable;
using Sacred.Core.CombatArts;
using Sacred.Inventory.Actors;

namespace Sacred.UI.CombatArts;

/// <summary>Character-bound art selection and local taskbar assignments. Casting stays with the host.</summary>
public sealed class CombatArtPanelState
{
    private readonly ushort?[] _quickSlots = new ushort?[SacredQuickSlotDefinitions.MaximumSlots];
    private SacredGameActor? _actor;
    public ushort? SelectedCode { get; private set; }
    public IReadOnlyList<ushort?> QuickSlots { get; }
    public IReadOnlyList<SacredCombatArtDefinition> Available { get; private set; } = [];

    public CombatArtPanelState() => QuickSlots = Array.AsReadOnly(_quickSlots);

    public void Bind(SacredGameActor? actor)
    {
        if (ReferenceEquals(_actor, actor)) return;
        if (_actor is not null) _actor.CombatArts.Changed -= RefreshAvailable;
        _actor = actor;
        if (_actor is not null) _actor.CombatArts.Changed += RefreshAvailable;
        RefreshAvailable();
        SelectedCode = null;
        Array.Clear(_quickSlots);
    }

    public int Rank(ushort code) => _actor?.CombatArts.Entries.GetValueOrDefault(code)?.TotalLevel ?? 0;
    public bool CanAssign(ushort code) => Rank(code) > 0 && Available.Any(art => art.Code == code);

    public bool Select(ushort code)
    {
        SelectedCode = CanAssign(code) ? code : null;
        return SelectedCode.HasValue;
    }

    public bool Assign(ushort code, int slot, int visibleSlots)
    {
        if ((uint)slot >= (uint)_quickSlots.Length || slot >= visibleSlots || !CanAssign(code)) return false;
        _quickSlots[slot] = code;
        SelectedCode = null;
        return true;
    }

    public void CancelSelection() => SelectedCode = null;

    private void RefreshAvailable()
    {
        if (_actor is null) { Available = []; return; }
        // Acquired native arts can include automatic transformations without a rune record.
        Available = Array.AsReadOnly(_actor.CombatArts.Available.Concat(_actor.CombatArts.Entries.Values.Select(entry =>
            new SacredCombatArtDefinition(entry.Code, SacredCombatArtDefinition.GetNameResource(entry.Code) ?? 0,
                entry.Kind, _actor.CharacterClass.ToMask(), [])))
            .DistinctBy(art => art.Code).OrderBy(art => art.Kind).ThenBy(art => art.Code).ToArray());
        if (SelectedCode is { } code && !CanAssign(code)) SelectedCode = null;
    }
}
