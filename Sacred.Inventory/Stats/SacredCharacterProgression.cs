using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.CombatArts;
using Sacred.Core.Pak.Creature;

namespace Sacred.Inventory.Stats;

/// <summary>Mutable allocations owned by a single creature, independent of its equipment.</summary>
public sealed class SacredCharacterProgression(SacredCreatureTemplate? template)
{
    private readonly Dictionary<SacredActorStat, int> _attributes = new();
    private readonly Dictionary<byte, int> _skills = template?.StartingSkills
        .Concat(template.Class == SacredCreatureClass.Hero ? Enumerable.Empty<byte>() : template.AvailableSkills.Take(6))
        .Where(id => id != 0)
        .Distinct().ToDictionary(id => id, _ => 1) ?? new();
    private readonly Dictionary<ushort, int> _combatArts = new();
    private FrozenDictionary<ushort, SacredCombatArtDefinition> _artDefinitions = FrozenDictionary<ushort, SacredCombatArtDefinition>.Empty;
    private ushort _level = 1;
    private bool _combatArtsConfigured;
    public SacredCreatureTemplate? Template { get; } = template;
    public ushort Level => _level;
    public IReadOnlyList<SacredCombatArtDefinition> AvailableCombatArts { get; private set; } = [];
    public event Action? Changed;
    public int GetAllocatedAttribute(SacredActorStat attribute) => _attributes.GetValueOrDefault(attribute);

    public void SetLevel(ushort level)
    {
        if (level == 0) throw new ArgumentOutOfRangeException(nameof(level));
        _level = level;
        Changed?.Invoke();
    }

    public void SetAllocatedAttribute(SacredActorStat attribute, int points)
    {
        if (attribute > SacredActorStat.Charisma || attribute < 0 || points < 0 || points > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(points));
        _attributes[attribute] = points;
        Changed?.Invoke();
    }

    public void SetSkill(byte id, int rank)
    {
        if (id == 0 || rank < 0 || rank > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(rank));
        if (Template is { } source && rank > 0 && !source.StartingSkills.Contains(id) && !source.AvailableSkills.Contains(id))
            throw new ArgumentException("This skill is unavailable in the creature template.", nameof(id));
        if (rank > 0 && !_skills.ContainsKey(id) && _skills.Count == 8)
            throw new InvalidOperationException("A creature has at most eight learned skills.");
        if (rank == 0) _skills.Remove(id); else _skills[id] = rank;
        Changed?.Invoke();
    }

    public void SetCombatArt(ushort code, int rank)
    {
        if (rank < 0 || rank > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(rank));
        if (rank > 0 && !_artDefinitions.ContainsKey(code))
            throw new ArgumentException("This combat art is unavailable to this creature.", nameof(code));
        if (rank == 0) _combatArts.Remove(code); else _combatArts[code] = rank;
        Changed?.Invoke();
    }

    public void ConfigureCombatArts(IEnumerable<SacredCombatArtDefinition> definitions)
    {
        var available = definitions.Where(art => art.IsAvailableTo(Template)).ToArray();
        var lookup = available.Concat(SacredCombatArtStartingAllocation.DefinitionsForCreature(Template))
            .DistinctBy(art => art.Code).ToFrozenDictionary(art => art.Code);
        if (_combatArts.Keys.Any(code => !lookup.ContainsKey(code)))
            throw new InvalidOperationException("The new pool excludes an acquired combat art.");
        _artDefinitions = lookup;
        AvailableCombatArts = Array.AsReadOnly(available);
        if (!_combatArtsConfigured)
        {
            foreach (var code in SacredCombatArtStartingAllocation.ForCreature(Template))
                if (lookup.ContainsKey(code)) _combatArts.TryAdd(code, 1);
            _combatArtsConfigured = true;
        }
        Changed?.Invoke();
    }

    internal SacredCombatArtKind? GetCombatArtKind(ushort code) => _artDefinitions.GetValueOrDefault(code)?.Kind;

    public SacredActorStats CreateStats()
    {
        var values = new Dictionary<SacredActorStat, double>();
        if (Template is { } source)
        {
            int[] bases = [source.Strength, source.Endurance, source.Dexterity, source.PhysicalRegeneration,
                source.MentalRegeneration, source.Charisma];
            for (var index = 0; index < bases.Length; index++)
                values[(SacredActorStat)index] = bases[index] + bases[index] * (Level - 1) / 10;
            values[SacredActorStat.MovementSpeed] = source.RunSpeed;
        }
        foreach (var (attribute, points) in _attributes)
            values[attribute] = values.GetValueOrDefault(attribute) + points;
        // CalcResults writes the six native results to unsigned words, including at debug levels.
        foreach (var attribute in Enum.GetValues<SacredActorStat>().Take(6))
            if (values.TryGetValue(attribute, out var value)) values[attribute] = unchecked((ushort)(int)value);
        return new(values, _skills, _combatArts);
    }

    internal void CopyTo(SacredCharacterProgression target)
    {
        target._level = _level;
        target._artDefinitions = _artDefinitions;
        target._combatArtsConfigured = _combatArtsConfigured;
        target.AvailableCombatArts = AvailableCombatArts;
        foreach (var entry in _attributes) target._attributes[entry.Key] = entry.Value;
        target._skills.Clear();
        foreach (var entry in _skills) target._skills[entry.Key] = entry.Value;
        target._combatArts.Clear();
        foreach (var entry in _combatArts) target._combatArts[entry.Key] = entry.Value;
        target.Changed?.Invoke();
    }
}
