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
    private readonly byte[] _values = new byte[SacredActorValuesLayout.Size];
    private readonly byte[] _health = new byte[GoldCreatureValuesLayout.Block128Size];
    private readonly SacredCreatureTemplate? _template;

    public WorldCreatureProgression(SacredCreatureTemplate template) : this(
        (template ?? throw new ArgumentNullException(nameof(template))).ItemType, template) { }
    /// <summary>Gold 0x586C00's missing-template path retains its native fallback
    /// basis, zero skills, level one and constructor HP 100.</summary>
    public WorldCreatureProgression(uint type) : this(type, null) { }
    private WorldCreatureProgression(uint type, SacredCreatureTemplate? template)
    {
        if (template?.Class == SacredCreatureClass.Hero)
            throw new ArgumentException("Hero construction requires native learned combat arts and hero initialization.", nameof(template));
        _template = template;
        BinaryPrimitives.WriteUInt32LittleEndian(_values, type);
        // Seed 0x586C00 copies the Basis in archive order, independently of its
        // first current-attribute assignment. CalcResults subsequently reads Basis.
        byte[] basis = template is null ? [25,15,20,20,10,5] :
            [template.Strength, template.Dexterity, template.PhysicalRegeneration,
                template.Endurance, template.Charisma, template.MentalRegeneration];
        for (var i = 0; i < basis.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(_values.AsSpan(66 + i * 2), basis[i]);
        for (var i = 0; template is not null && i < 8; i++)
        {
            var skill = i < 2 ? template.StartingSkills[i] : template.AvailableSkills[i - 2];
            _values[28 + i] = skill;
            if (skill != 0) _values[36 + i] = 1;
        }
        SetLevelWord(1);
        SetHitPoints(100);
        // The original constructor still has reference/owner zero. Its first
        // CalcResults and SetLevel leave regeneration zero and preserve seeded ranks.
    }

    public ushort Level => BinaryPrimitives.ReadUInt16LittleEndian(_values.AsSpan(78));
    public uint Experience => BinaryPrimitives.ReadUInt32LittleEndian(_values.AsSpan(4));

    public void SetLevel(ushort resolvedLevel, SacredCreatureBalance balance, uint difficulty, uint players,
        float bossFactor = 1, bool runtimeHealthBonus = false, ushort? strength = null, ushort? endurance = null,
        byte calculatedAttributeBonusMask = 0)
    {
        if (resolvedLevel == 0) throw new ArgumentOutOfRangeException(nameof(resolvedLevel));
        if (_template is null) throw new InvalidOperationException("Missing-template level recalculation requires the native fallback attribute calculation.");
        SetLevelWord(resolvedLevel);
        BinaryPrimitives.WriteUInt32LittleEndian(_values.AsSpan(4), resolvedLevel < 2 ? 0 :
            SacredCreatureProgressionRules.ExperienceAtLevel((ushort)(resolvedLevel - 1)));
        _values.AsSpan(36, 8).Clear();
        var active = SacredCreatureProgressionRules.ActiveSkillCount(resolvedLevel);
        for (var i = 0; i < active; i++)
            if (_values[28 + i] != 0) _values[36 + i] = unchecked((byte)(resolvedLevel / 3));
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
