using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sacred.Core.CombatArts;
using Sacred.Core.Pak.Creature;

namespace Sacred.Inventory.CombatArts;

/// <summary>Actor-owned acquired arts. Definitions are shared; rank, flags and timers
/// are per actor. Equipment updates only the separate bonus byte.</summary>
public sealed class SacredActorCombatArts
{
    private readonly SacredCreatureTemplate? _template;
    private readonly Dictionary<ushort, SacredActorCombatArt> _entries = new();
    private readonly HashSet<ushort> _nativeCodes = new();
    private FrozenDictionary<ushort, SacredCombatArtDefinition> _definitions = FrozenDictionary<ushort, SacredCombatArtDefinition>.Empty;
    private bool _configured;

    public IReadOnlyDictionary<ushort, SacredActorCombatArt> Entries { get; }
    public IReadOnlyList<SacredCombatArtDefinition> Available { get; private set; } = [];
    public event Action? Changed;

    public SacredActorCombatArts(SacredCreatureTemplate? template)
    {
        _template = template;
        Entries = new ReadOnlyDictionary<ushort, SacredActorCombatArt>(_entries);
    }

    public void Configure(IEnumerable<SacredCombatArtDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var available = definitions.Where(art => art.IsAvailableTo(_template)).ToArray();
        var lookup = available.Concat(SacredCombatArtStartingAllocation.DefinitionsForCreature(_template))
            .DistinctBy(art => art.Code).ToFrozenDictionary(art => art.Code);
        if (_entries.Keys.Any(code => !_nativeCodes.Contains(code) && !lookup.ContainsKey(code)))
            throw new InvalidOperationException("The new pool excludes an acquired combat art.");
        _definitions = lookup;
        Available = Array.AsReadOnly(available);
        if (!_configured)
        {
            foreach (var code in SacredCombatArtStartingAllocation.ForCreature(_template))
                if (lookup.TryGetValue(code, out var definition))
                    _entries.TryAdd(code, new(definition.Kind, code, 1, 0));
            _configured = true;
        }
        Changed?.Invoke();
    }

    public void SetLevel(ushort code, int rank)
    {
        if (rank < 0 || rank > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(rank));
        if (rank == 0)
        {
            _entries.Remove(code);
            _nativeCodes.Remove(code);
        }
        else
        {
            var existing = _entries.GetValueOrDefault(code);
            if (!_definitions.TryGetValue(code, out var definition) && !_nativeCodes.Contains(code))
                throw new ArgumentException("This combat art is unavailable to this creature.", nameof(code));
            _entries[code] = existing is { } entry
                ? entry with { BaseLevel = (byte)rank } : new(definition!.Kind, code, (byte)rank, 0);
        }
        Changed?.Invoke();
    }

    /// <summary>Imports resolved native state, including NPC and hidden arts without
    /// player rune definitions. Player rune eligibility is an acquisition rule.</summary>
    public void Import(SacredActorCombatArt entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind is not (SacredCombatArtKind.Spell or SacredCombatArtKind.SpecialMove) ||
            _definitions.TryGetValue(entry.Code, out var definition) && definition.Kind != entry.Kind)
            throw new ArgumentException("The native entry has an invalid or conflicting art kind.", nameof(entry));
        _entries[entry.Code] = entry;
        _nativeCodes.Add(entry.Code);
        Changed?.Invoke();
    }

    public void SetTimers(ushort code, float maximum, float factor, float remaining)
    {
        if (!float.IsFinite(maximum) || !float.IsFinite(factor) || !float.IsFinite(remaining) ||
            maximum < 0 || factor < 0 || remaining < 0)
            throw new ArgumentOutOfRangeException(nameof(remaining));
        if (!_entries.TryGetValue(code, out var entry)) throw new ArgumentException("The art is not acquired.", nameof(code));
        _entries[code] = entry with { TimerMaximum = maximum, TimerFactor = factor, Timer = remaining };
        Changed?.Invoke();
    }

    internal SacredCombatArtKind? GetKind(ushort code) => _entries.GetValueOrDefault(code)?.Kind ?? _definitions.GetValueOrDefault(code)?.Kind;
    internal Dictionary<ushort, int> BaseLevels() => _entries.ToDictionary(pair => pair.Key, pair => (int)pair.Value.BaseLevel);

    internal void ApplyEquipmentBonuses(IReadOnlyDictionary<ushort, int> bonuses)
    {
        foreach (var (code, entry) in _entries.ToArray())
            _entries[code] = entry with { BonusLevel = checked((byte)bonuses.GetValueOrDefault(code)) };
    }

    internal void CopyTo(SacredActorCombatArts target)
    {
        target._configured = _configured;
        target._definitions = _definitions;
        target.Available = Available;
        target._entries.Clear();
        foreach (var entry in _entries) target._entries.Add(entry.Key, entry.Value);
        target._nativeCodes.Clear();
        target._nativeCodes.UnionWith(_nativeCodes);
    }
}
