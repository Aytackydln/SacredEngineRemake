using System.Buffers.Binary;
using Sacred.Assets.Executable;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold CreateNPC 0x4835D5..0x483605: assigned level (tag 0x90) calls
/// SetLevel(level,1,0) when it is not below the current level, then stores its
/// low byte at values +80. Initial empty-equipment, KI-zero NPCs only.</summary>
public static class WorldCreatureScriptLevel
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, SacredCreatureTemplate template,
        int input, WorldCreatureScalarResultAssets results, SacredCreatureLevelBalance levels,
        SacredCreatureBalance progression, SacredCreatureLevelDefinitions definitions,
        uint difficulty, uint localReference, bool networking, ushort sectorMaximumLevel, uint players)
        => Initialize(actor, template, input, results, levels, progression, definitions, difficulty, localReference,
            networking, sectorMaximumLevel, players, assigned: true);

    /// <summary>Gold 0x4843F1/0x48475A: positive 0x1F requests call SetLevel
    /// with ordinary difficulty/multiplayer adjustment, including lower levels.
    /// They do not write the assigned-input marker at values +80.</summary>
    public static WorldCreatureInitialActor ApplyRequested(WorldCreatureInitialActor actor, SacredCreatureTemplate template,
        int input, WorldCreatureScalarResultAssets results, SacredCreatureLevelBalance levels,
        SacredCreatureBalance progression, SacredCreatureLevelDefinitions definitions,
        uint difficulty, uint localReference, bool networking, ushort sectorMaximumLevel, uint players)
        => Initialize(actor, template, input, results, levels, progression, definitions, difficulty, localReference,
            networking, sectorMaximumLevel, players, assigned: false);

    private static WorldCreatureInitialActor Initialize(WorldCreatureInitialActor actor, SacredCreatureTemplate template,
        int input, WorldCreatureScalarResultAssets results, SacredCreatureLevelBalance levels,
        SacredCreatureBalance progression, SacredCreatureLevelDefinitions definitions,
        uint difficulty, uint localReference, bool networking, ushort sectorMaximumLevel, uint players, bool assigned)
    {
        if (input == 0) return actor;
        var special = !assigned && input == 1000;
        if (special)
        {
            input = 1; // Gold 0x4843C2 -> 0x549500(1,0) -> SetLevel(1,1,0).
            actor = actor with { BehaviorFlags = actor.BehaviorFlags | 0x80000 };
        }
        if (actor.Base.ObjectReference <= 16 || actor.Base.ObjectType != template.ItemType ||
            actor.KiMode != 0 || actor.EiMode != 0 || actor.PaletteMask != 0 || actor.EquipmentReferences.Any(value => value != 0) ||
            (actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 ||
            template.Class == SacredCreatureClass.Hero)
            throw new NotSupportedException("Script level requires an initial ordinary unloaded, empty-equipment NPC.");
        var values = (byte[])actor.Component.Values.Clone();
        var health = (byte[])actor.Component.Health.Clone();
        var behavior = actor.BehaviorFlags;
        var valid = actor.Component.ResultValid;
        if (!assigned || BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(78)) <= input)
        {
            var selection = WorldCreatureLevel.Resolve(unchecked((ushort)input), template,
                new(actor.Base.ObjectReference, difficulty, networking, behavior, 0, sectorMaximumLevel, 0,
                    SkipDifficultyAdjustment: assigned || special, TypeMaximumOverride: definitions.MaximumOverride(template.ItemType)), levels, progression);
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(78), selection.Level);
            var experience = selection.Level < 2 ? 0 : results.StartupPrecision == SacredX87Precision.Single
                ? SacredStartupExperience.AtLevel(unchecked((ushort)(selection.CalculatedLevel - 1))) : selection.Experience;
            BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(4), experience);
            values.AsSpan(36, 8).Clear();
            for (var slot = 0; slot < selection.ActiveSkillCount; slot++)
                if (values[28 + slot] != 0) values[36 + slot] = selection.SkillRank;
            behavior = (behavior & 0x7fffffff) | ((template.Flags & 0x40) != 0 ? 0x08000000u : 0);
            var calculated = results.Calculate(values, template,
                new(template.ItemType, actor.Base.ObjectReference, difficulty, false,
                    BinaryPrimitives.ReadUInt32LittleEndian(health.AsSpan(16)), actor.Base.Flags, 0,
                    actor.Component.CombatProfile, 0, 0, false, default, localReference, results.StartupPrecision), []);
            values = calculated.Values;
            BinaryPrimitives.WriteUInt32LittleEndian(health.AsSpan(16), calculated.Regeneration);
            results.CalculateHealth(values, health, new(actor.Base.Owner, localReference, difficulty, networking,
                players, behavior, 0, 0, false, false, results.StartupPrecision)
                { ObjectType = template.ItemType, CreatureClass = template.Class });
            var maximum = BinaryPrimitives.ReadInt32LittleEndian(health.AsSpan(4));
            if (maximum == 0) maximum = 100;
            for (var at = 0; at < 12; at += 4) BinaryPrimitives.WriteInt32LittleEndian(health.AsSpan(at), maximum);
            _ = WorldCreatureConstructionEvents.LevelChanged(true, actor.KiMode, actor.PaletteMask);
            valid = 1;
        }
        if (assigned) values[80] = unchecked((byte)input);
        return actor with { Component = new(values, health, actor.Component.CombatProfile, valid), BehaviorFlags = behavior };
    }
}
