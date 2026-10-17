using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Sacred.Core.CombatArts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.Inventory.CombatArts;

namespace Sacred.Inventory.Stats;

/// <summary>Mutable allocations owned by a single creature, independent of its equipment.</summary>
public sealed class SacredCharacterProgression
{
    private readonly Dictionary<SacredActorStat, int> _attributes = new();
    private readonly Dictionary<byte, int> _skills;
    private ushort _level = 1;

    public SacredCharacterProgression(SacredCreatureTemplate? template)
    {
        Template = template;
        MovementProfileSelector = template?.HorseClass ?? 0;
        BaseStats = SacredActorBaseStats.FromTemplate(template);
        _skills = template?.StartingSkills
            .Concat(template.Class == SacredCreatureClass.Hero ? Enumerable.Empty<byte>() : template.AvailableSkills.Take(6))
            .Where(id => id != 0).Distinct().ToDictionary(id => id, _ => 1) ?? new();
        CombatArts = new(template);
        CombatArts.Changed += () => Changed?.Invoke();
    }

    public SacredCreatureTemplate? Template { get; }
    public SacredActorBaseStats BaseStats { get; private set; }
    public SacredActorCombatArts CombatArts { get; }
    /// <summary>Original imported value block, including results not yet calculated by the remake.</summary>
    public SacredActorValuesLayout? NativeValues { get; private set; }
    public byte MovementProfileSelector { get; private set; }
    public uint Experience { get; private set; }
    public uint Gold { get; private set; }
    public ushort RemainingSkillPoints { get; private set; }
    public ushort Level => _level;
    public IReadOnlyList<SacredCombatArtDefinition> AvailableCombatArts => CombatArts.Available;
    public event Action? Changed;
    public int GetAllocatedAttribute(SacredActorStat attribute) => _attributes.GetValueOrDefault(attribute);

    /// <summary>Imports the native value block only, without following native
    /// pointers or interpreting a complete save. Validates before modifying this actor.</summary>
    public void ImportNativeValues(ReadOnlySpan<byte> bytes) => ImportValues(bytes, true);

    /// <summary>Imports values belonging to an identified replicated object. Its current
    /// appearance template is supplied by the object's resource, independently of the retained value block.</summary>
    public void ImportNativeObjectValues(ReadOnlySpan<byte> bytes) => ImportValues(bytes, false);

    private void ImportValues(ReadOnlySpan<byte> bytes, bool requireTemplateMatch)
    {
        if (bytes.Length != SacredActorValuesLayout.Size)
            throw new ArgumentException("A native actor value block must contain exactly 238 bytes.", nameof(bytes));
        var source = MemoryMarshal.Read<SacredActorValuesLayout>(bytes);
        if (source.Level == 0 || requireTemplateMatch && Template is { } template && source.ItemType != template.ItemType)
            throw new ArgumentException("The value block does not match this actor's template or level.", nameof(bytes));
        var skills = new Dictionary<byte, int>();
        for (var index = 0; index < 8; index++)
        {
            var id = source.SkillIds[index];
            if (id != 0 && !skills.TryAdd(id, source.SkillLevels[index]))
                throw new ArgumentException("The value block contains duplicate learned skills.", nameof(bytes));
        }
        NativeValues = source;
        MovementProfileSelector = source.HorseClass;
        BaseStats = SacredActorBaseStats.FromNativeValues(source);
        _level = source.Level;
        Experience = source.Experience;
        Gold = source.Gold;
        RemainingSkillPoints = source.RemainingSkillPoints;
        _attributes.Clear();
        for (var index = 0; index < 6; index++) _attributes[(SacredActorStat)index] = source.AttributeAllocations[index];
        _skills.Clear();
        foreach (var entry in skills) _skills.Add(entry.Key, entry.Value);
        Changed?.Invoke();
    }

    /// <summary>Runtime Basis can differ from the archive template; it is independent of allocations.</summary>
    public void SetBaseStats(SacredActorBaseStats values)
    {
        BaseStats = values ?? throw new ArgumentNullException(nameof(values));
        Changed?.Invoke();
    }

    public void SetLevel(ushort level)
    {
        if (level == 0) throw new ArgumentOutOfRangeException(nameof(level));
        _level = level;
        Changed?.Invoke();
    }

    /// <summary>Replace received or saved progression counters without granting skills or calculating rewards.</summary>
    public void SetProgression(ushort level, uint experience, uint gold, ushort remainingSkillPoints)
    {
        if (level == 0) throw new ArgumentOutOfRangeException(nameof(level));
        _level = level;
        Experience = experience;
        Gold = gold;
        RemainingSkillPoints = remainingSkillPoints;
        Changed?.Invoke();
    }

    /// <summary>Gold compact values carry progression inputs, but omit base attributes and calculated speeds.</summary>
    public void ImportNativeProgression(ushort level, uint experience, uint gold, ushort remainingSkillPoints,
        ReadOnlySpan<byte> allocations, ReadOnlySpan<byte> skillIds, ReadOnlySpan<byte> skillLevels, byte horseClass)
    {
        if (level == 0 || allocations.Length != 8 || skillIds.Length != 8 || skillLevels.Length != 8)
            throw new ArgumentException("Invalid compact character progression.");
        var skills = new Dictionary<byte, int>();
        for (var index = 0; index < 8; index++)
            if (skillIds[index] != 0 && !skills.TryAdd(skillIds[index], skillLevels[index]))
                throw new ArgumentException("Duplicate native learned skill.");
        _level = level;
        Experience = experience;
        Gold = gold;
        RemainingSkillPoints = remainingSkillPoints;
        MovementProfileSelector = horseClass;
        _attributes.Clear();
        for (var index = 0; index < 6; index++) _attributes[(SacredActorStat)index] = allocations[index];
        _skills.Clear();
        foreach (var entry in skills) _skills.Add(entry.Key, entry.Value);
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

    public void SetCombatArt(ushort code, int rank) => CombatArts.SetLevel(code, rank);

    public void ConfigureCombatArts(IEnumerable<SacredCombatArtDefinition> definitions) => CombatArts.Configure(definitions);

    internal SacredCombatArtKind? GetCombatArtKind(ushort code) => CombatArts.GetKind(code);

    public SacredActorStats CreateStats()
    {
        var values = new Dictionary<SacredActorStat, double>();
        for (var index = 0; index < 6; index++)
        {
            var attribute = (SacredActorStat)index;
            var basis = BaseStats[attribute];
            values[attribute] = basis + basis * (Level - 1) / 10;
        }
        foreach (var (attribute, points) in _attributes)
            values[attribute] = values.GetValueOrDefault(attribute) + points;
        // CalcResults writes the six native results to unsigned words, including at debug levels.
        foreach (var attribute in Enum.GetValues<SacredActorStat>().Take(6))
            if (values.TryGetValue(attribute, out var value)) values[attribute] = unchecked((ushort)(int)value);
        return new(values, _skills, CombatArts.BaseLevels());
    }

    internal void CopyTo(SacredCharacterProgression target)
    {
        target._level = _level;
        target.BaseStats = BaseStats;
        target.NativeValues = NativeValues;
        target.MovementProfileSelector = MovementProfileSelector;
        target.Experience = Experience;
        target.Gold = Gold;
        target.RemainingSkillPoints = RemainingSkillPoints;
        CombatArts.CopyTo(target.CombatArts);
        foreach (var entry in _attributes) target._attributes[entry.Key] = entry.Value;
        target._skills.Clear();
        foreach (var entry in _skills) target._skills[entry.Key] = entry.Value;
        target.Changed?.Invoke();
    }
}
