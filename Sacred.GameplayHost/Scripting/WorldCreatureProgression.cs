using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>New non-hero creature's compact progression inputs. The factory supplies a
/// resolved/capped level and health modifiers; equipment and combat result calculation
/// remain actor services. Incomplete calculated stats are never published as full values.</summary>
public sealed class WorldCreatureProgression
{
    private readonly byte[] _values;
    private readonly byte[] _health = new byte[GoldCreatureValuesLayout.Block128Size];
    private readonly SacredCreatureTemplate? _template;

    public WorldCreatureProgression(SacredCreatureTemplate template) : this(
        (template ?? throw new ArgumentNullException(nameof(template))).ItemType, template) { }
    /// <summary>Gold 0x586C00's missing-template path retains its native fallback
    /// basis, zero skills, level one and constructor HP 100.</summary>
    public WorldCreatureProgression(uint type) : this(type, null) { }
    private WorldCreatureProgression(uint type, SacredCreatureTemplate? template)
    {
        _template = template;
        _values = WorldCreatureComponentSeed.Create(type, template);
        SetHitPoints(100);
        // The original constructor still has reference/owner zero. Its first
        // CalcResults and SetLevel leave regeneration zero and preserve seeded ranks.
    }

    public ushort Level => BinaryPrimitives.ReadUInt16LittleEndian(_values.AsSpan(78));
    public uint Experience => BinaryPrimitives.ReadUInt32LittleEndian(_values.AsSpan(4));

    /// <summary>Actor-aware native SetLevel selection. Callers must supply runtime
    /// additions/sector limits at this invocation; script input alone is insufficient.</summary>
    public void SetLevel(ushort input, WorldCreatureLevelContext context, SacredCreatureLevelBalance levels,
        SacredCreatureBalance balance, uint players, float bossFactor = 1, bool runtimeHealthBonus = false,
        ushort? strength = null, ushort? endurance = null, byte calculatedAttributeBonusMask = 0)
    {
        var selected = WorldCreatureLevel.Resolve(input, _template, context, levels, balance);
        SetResolvedLevel(selected, context.ObjectReference > 16, balance, context.Difficulty, players,
            bossFactor, runtimeHealthBonus, strength, endurance, calculatedAttributeBonusMask);
    }

    public void SetLevel(ushort resolvedLevel, SacredCreatureBalance balance, uint difficulty, uint players,
        float bossFactor = 1, bool runtimeHealthBonus = false, ushort? strength = null, ushort? endurance = null,
        byte calculatedAttributeBonusMask = 0)
    {
        if (resolvedLevel == 0) throw new ArgumentOutOfRangeException(nameof(resolvedLevel));
        SetResolvedLevel(new(resolvedLevel), true, balance, difficulty, players,
            bossFactor, runtimeHealthBonus, strength, endurance, calculatedAttributeBonusMask);
    }

    private void SetResolvedLevel(WorldCreatureLevelSelection selected, bool updateSkills, SacredCreatureBalance balance,
        uint difficulty, uint players, float bossFactor, bool runtimeHealthBonus, ushort? strength,
        ushort? endurance, byte calculatedAttributeBonusMask)
    {
        if (_template is null) throw new InvalidOperationException("Missing-template level recalculation requires the native fallback attribute calculation.");
        var resolvedLevel = selected.Level;
        // Zero is a valid selection for special actors/zero caps; health calculation's
        // supported nonzero-level domain must not silently substitute a level here.
        if (resolvedLevel == 0) throw new InvalidOperationException("Selected level zero requires native zero-level value calculation.");
        SetLevelWord(resolvedLevel);
        BinaryPrimitives.WriteUInt32LittleEndian(_values.AsSpan(4), selected.Experience);
        if (updateSkills)
        {
            _values.AsSpan(36, 8).Clear();
            for (var i = 0; i < selected.ActiveSkillCount; i++)
                if (_values[28 + i] != 0) _values[36 + i] = selected.SkillRank;
        }
        var currentStrength = strength ?? Grow(_template.Strength, resolvedLevel);
        var currentEndurance = endurance ?? Grow(_template.Endurance, resolvedLevel);
        _values[26] = calculatedAttributeBonusMask;
        var vitality = 0;
        for (var i = 0; i < 8; i++)
            if (_values[28 + i] == 10) { vitality = _values[36 + i]; break; }
        var hp = SacredCreatureHealth.Calculate(resolvedLevel, _template.Strength, _template.Endurance,
            currentStrength, currentEndurance, (byte)vitality, balance, difficulty, false, players, bossFactor, runtimeHealthBonus);
        SetHitPoints(hp);
        BinaryPrimitives.WriteUInt32LittleEndian(_health.AsSpan(16), (uint)currentEndurance * 2);
    }

    public GoldCreatureValuesState Write(ushort opcode)
    {
        if (GoldCreatureValuesLayout.IsFull(opcode))
            throw new InvalidOperationException("Full values require the actor's calculated combat result block.");
        return GoldCreatureValuesWriter.Write(opcode, _values, _health);
    }

    private void SetHitPoints(int value)
    {
        // SetLevel refills TP[0..2], using 100 when CalcResults supplies zero.
        if (value == 0) value = 100;
        for (var i = 0; i < 3; i++) BinaryPrimitives.WriteInt32LittleEndian(_health.AsSpan(i * 4), value);
    }
    private void SetLevelWord(ushort level) => BinaryPrimitives.WriteUInt16LittleEndian(_values.AsSpan(78), level);
    private static ushort Grow(byte basis, ushort level) => unchecked((ushort)(basis + basis * ((int)level - 1) / 10));
}
