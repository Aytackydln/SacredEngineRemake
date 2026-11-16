using System.Buffers.Binary;
using Sacred.Assets.Executable;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Composed NPC scalar calculation through Gold 0x57D1F9, before health
/// and notification. Reject unsupported runtime states before changing inputs.
/// This returns private values; it does not allocate, publish or finish startup.</summary>
public static class WorldCreatureScalarResults
{
    public static WorldCreatureScalarResult Calculate(byte[] inputs, SacredCreatureTemplate? template,
        WorldCreatureScalarResultContext context, IReadOnlyList<WorldCreatureScalarEquipment> equipment,
        SacredCreatureResultDefinitions initialization, SacredCreatureCombatDefinitions combat,
        SacredCreatureResultFinalizationDefinitions finalization, SacredCreatureResultBalanceLayout resultBalance,
        SacredCreatureSkillBalanceLayout skillBalance, SacredCreatureEquipmentLevelBalanceLayout equipmentBalance, SacredMovementBalance? movement = null)
    {
        if (inputs.Length != SacredActorValuesLayout.Size) throw new ArgumentException("Expected 238-byte actor inputs.", nameof(inputs));
        if (context.Precision is not (SacredX87Precision.Single or SacredX87Precision.Extended))
            throw new NotSupportedException("Composed scalar results require a compared 24-bit or 64-bit x87 mode.");
        var constructor = context.Reference == 0 && equipment.Count == 0 && template is not null &&
            context.Resource == template.ItemType;
        if (context.Reference <= 16 && !constructor || context.Reference == context.LocalReference)
            throw new NotSupportedException("Scalar calculation requires a nonlocal assigned NPC or its empty unassigned constructor state.");
        if (template?.Class == SacredCreatureClass.Horse && equipment.Count != 0)
            throw new NotSupportedException("Horse equipment requires its native slot/result comparison.");
        if (context.Mounted || context.LearnedCombatArtCount != 0 || context.ActiveEffectCount != 0)
            throw new NotSupportedException("Mounted actors, learned combat arts and active effects require their native result bodies.");
        if (template?.InnateBonuses.Any(row => row.CreatureClass == 0 && row.SourceOperation is >= 23 and <= 36) == true)
            throw new NotSupportedException("Active innate powers require the original legacy calculation.");
        if (equipment.Select(row => row.Slot).Distinct().Count() != equipment.Count || equipment.Any(row => row.Slot >= 18 || row.Records.Count == 0))
            throw new ArgumentException("Expected unique on-foot equipment slots with a main record.", nameof(equipment));
        var level = BinaryPrimitives.ReadUInt16LittleEndian(inputs.AsSpan(78));
        foreach (var slot in equipment)
            for (var index = 0; index < slot.Records.Count; index++)
            {
                var record = slot.Records[index];
                if (record.Values.Length != Sacred.Core.Pak.Weapon.SacredEquipmentInstanceLayout.Size || index > 8)
                    throw new ArgumentException("Expected concrete main/merged equipment records.", nameof(equipment));
                if (index != 0 && slot.Records[0].Values[12 + index - 1] is not (1 or 2 or 3 or 4)) continue;
                if (record.SharedSetType != 0) throw new NotSupportedException("Equipment set tracking requires the native component/set state.");
                if (record.Values[25] > level && (!IsArmor(record) || !record.HasArmorModifierQuery))
                    throw new NotSupportedException("Overlevel equipment requires the original level penalties and resolved armor modifier query.");
                var spellThreshold = record.Values[25] * (double)equipmentBalance.SpellBonusItemLevelFactor;
                if (context.Precision == SacredX87Precision.Single) spellThreshold = (float)spellThreshold;
                if ((int)record.Category is not (5 or 27 or 6 or 13 or 17 or 18 or 19 or 21 or 22 or 23 or 24 or 25) &&
                    Enumerable.Range(0, 4).Any(bonus => (BinaryPrimitives.ReadUInt16LittleEndian(record.Values.AsSpan(58 + bonus * 2)) & 0x2000) != 0) &&
                    level < spellThreshold)
                    throw new NotSupportedException("Spell-bonus equipment requires the original cast-speed level penalty.");
            }
        var values = (byte[])inputs.Clone();
        var profile = combat.Profile(context.CombatProfile);
        var physicalResource = context.ActorTypeAssigned ? context.Resource : 0;
        var regeneration = WorldCreatureResultInitialization.Apply(values, physicalResource, context.Reference,
            context.Difficulty, context.DifficultyBoostApplies, template, initialization) ?? context.Regeneration;
        if (context.ActorTypeAssigned && template?.Class == SacredCreatureClass.Horse)
        {
            if (movement is null) throw new NotSupportedException("Horse results require original Balance.bin movement profiles.");
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(52), 140);
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(54), 120);
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(56), unchecked((ushort)movement.GetProfile(values[80]).MovementSpeedPercent));
        }
        var pending = new int[6];
        uint equipmentFlags = 0;
        float armorMovement = 1;
        var unarmed = context.Skills.MainUsage == 0 && !context.Skills.OffhandPresent;
        foreach (var slot in equipment.OrderBy(row => row.Slot))
            for (var index = 0; index < slot.Records.Count; index++)
            {
                if (index != 0 && slot.Records[0].Values[12 + index - 1] is not (1 or 2 or 3 or 4)) continue;
                var record = slot.Records[index];
                if (IsArmor(record))
                {
                    byte rank = 0;
                    for (var skill = 0; skill < 8; skill++)
                        if (values[28 + skill] == 11) { rank = (byte)Math.Min(255, values[36 + skill] + values[44 + skill]); break; }
                    armorMovement = Math.Min(armorMovement, SacredCreatureArmorLevelPenalty.Calculate(context.Precision,
                        level, rank, record.Values[25], record.ArmorModifier));
                }
                equipmentFlags |= SacredCreatureEquipmentContributions.Apply(context.Precision, values, pending, record.Values, record.Category,
                    new(context.Resource, unarmed, index != 0, 0, profile));
            }
        SacredCreatureBaseResults.Apply(context.Precision, values, pending, context.Skills.Skill7Weapon, profile, resultBalance);
        SacredCreatureCombatAdjustments.ApplyActorFlags(values, context.ActorFlags);
        if (template is not null) SacredCreatureInnateBonuses.Apply(context.Precision, values, template.InnateBonuses, unarmed, context.Skills.Skill7Weapon);
        SacredCreatureCombatAdjustments.ApplyUnarmedFallback(context.Precision, values, unarmed);
        regeneration = SacredCreaturePassiveSkills.Apply(context.Precision, values, regeneration, 0, context.Skills, skillBalance);
        SacredCreatureResultFinalization.Apply(context.Precision, values, context.ActorFlags, context.ComponentFlags, context.ActorTypeAssigned && template?.Class == SacredCreatureClass.Horse,
            finalization.UsesSlowSpeedFloor(physicalResource), finalization.SharesCombatArtRegeneration(physicalResource), armorMovement);
        return new(values, regeneration, equipmentFlags);
    }

    private static bool IsArmor(WorldCreatureScalarEquipmentRecord record) => (int)record.Category is 6 or 13 or 17 or 18 or 19 or 21 or 22 or 23 or 24 or 25;
}
