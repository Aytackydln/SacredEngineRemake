using System.Buffers.Binary;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>54B3D0/585260 -> local 5796A0, before saved inventory exists.
/// Retains actual lists and applies original scalar/timer normalization; it
/// does not install equipment, place the root or authorize readiness.</summary>
public static class WorldCreatureSavedValueImport
{
    public static WorldCreatureInitialActor Restore(WorldCreatureInitialActor initial, SacredSavedCreature saved,
        SacredCreatureTemplate template, WorldCreatureScalarResultAssets results, SacredSavedCreatureProfiles profiles,
        SacredCreatureArtTimerInputs arts, uint difficulty)
    {
        if (initial.RestoredPhysical is null || initial.RestoredCreature is not null || !initial.Node.Active ||
            template.Class != SacredCreatureClass.Hero || initial.Base.ObjectReference is 0 or > 16 ||
            initial.Base.ObjectType != saved.BaseState.Resource || saved.BaseState.ObjectReference != initial.Base.ObjectReference ||
            saved.Values.Count28 != 0 || saved.Count9 != 0 || saved.Count68 != 0 || saved.MountPartnerReference != 0 ||
            initial.EquipmentReferences.Any(value => value != 0))
            throw new NotSupportedException("Saved value import requires the current physical hero before inventory, with no mount or active status/extra lists.");
        var values = saved.Values.Block08.ToArray();
        if (BinaryPrimitives.ReadUInt32LittleEndian(values) != saved.BaseState.Resource || difficulty > 2)
            throw new NotSupportedException("Saved hero requires a matching component resource and a compared non-boost difficulty.");
        var experience = BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(4));
        var level = LevelOnExperience(experience);
        if (level != saved.Values.Level)
            throw new NotSupportedException("Saved level differs from XP; the original repair/level-change continuation is required.");
        BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(78), level);
        var profile = profiles.Profile(saved.BaseState.Resource);
        var state = new WorldCreatureRestoredState(saved, []);
        // Main-item lookups fail before the saved object list is constructed.
        // The offhand-presence predicate still tests its raw reference word.
        var skills = new SacredCreatureSkillContext(false, false, false, false, false, false, 0,
            saved.GetEquipmentReference(SacredSavedCreatureLayout.RightHandEquipmentIndex) != 0, false);
        var context = new WorldCreatureScalarResultContext(saved.BaseState.Resource, initial.Base.ObjectReference, difficulty,
            false, BinaryPrimitives.ReadUInt32LittleEndian(saved.Values.Block128[16..]), initial.Base.Flags, 0, profile,
            saved.Values.Count22, saved.Values.Count28, false, skills, initial.Base.ObjectReference, results.StartupPrecision)
            { SavedHeroArts = arts };
        var calculated = results.Calculate(values, template, context, []);
        var health = saved.Values.Block128.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(health.AsSpan(16), calculated.Regeneration);
        // This read's CalcResults(false,true) does not recalculate/refill HP.
        var behavior = (state.Word(SacredSavedCreatureLayout.RelationClassOffset + 4) & 0x7fffffff) |
            ((template.Flags & 0x40) != 0 ? 0x08000000u : 0);
        return initial with
        {
            Component = new(calculated.Values, health, profile, 1),
            RestoredCreature = new(saved, calculated.CombatArts ?? throw new InvalidDataException("Hero art normalization missing.")),
            RelationClass = state.Word(SacredSavedCreatureLayout.RelationClassOffset), BehaviorFlags = behavior,
            LocomotionAction = state.Word(SacredSavedCreatureLayout.LocomotionActionOffset),
            RuntimeFlags = saved.Word200 & ~0x10000000u,
            Flags1FC = state.Short(SacredSavedCreatureLayout.Flags1FCOffset),
            ThinkInterval = saved.Thinking.Interval, ThinkElapsed = saved.Thinking.Elapsed,
            KiMode = saved.Thinking.KiMode, EiMode = saved.Thinking.EiMode,
            KiState = saved.CreaturePrefix.Slice(SacredSavedCreatureLayout.BlockFCOffset - SacredSavedCreatureLayout.MarkerOffset, 84).ToArray(),
            EiState = saved.CreaturePrefix.Slice(SacredSavedCreatureLayout.Block150Offset - SacredSavedCreatureLayout.MarkerOffset, 84).ToArray(),
            ActionState = saved.CreaturePrefix.Slice(SacredSavedCreatureLayout.Block2EDOffset - SacredSavedCreatureLayout.MarkerOffset, 173).ToArray(),
            ScriptGroup = state.Word(SacredSavedCreatureLayout.ScriptGroupOffset), DialogIndex = unchecked((uint)saved.DialogIndex),
            PartnerReference = saved.MountPartnerReference,
            Flags2B7 = (ushort)(state.Short(SacredSavedCreatureLayout.Flags2B7Offset) & ~0x40),
            EquipmentSelector = state.Short(SacredSavedCreatureLayout.EquipmentSelectorOffset),
            SpellParameter = state.Short(SacredSavedCreatureLayout.SpellParameterOffset),
            SpellSelector = saved.CreaturePrefix[SacredSavedCreatureLayout.SpellSelectorOffset - SacredSavedCreatureLayout.MarkerOffset],
            ScriptInitialPosition = new(unchecked((int)state.Word(SacredSavedCreatureLayout.ScriptInitialPositionOffset + 4)),
                unchecked((int)state.Word(SacredSavedCreatureLayout.ScriptInitialPositionOffset + 8)),
                saved.CreaturePrefix[SacredSavedCreatureLayout.ScriptInitialPositionOffset + 12 - SacredSavedCreatureLayout.MarkerOffset],
                state.Short(SacredSavedCreatureLayout.ScriptInitialPositionOffset)),
            RandomTail = state.Short(SacredSavedCreatureLayout.Word574Offset)
        };
    }

    public static ushort LevelOnExperience(uint experience)
    {
        for (ushort level = 1; level <= 100; level++)
            if (experience < SacredStartupExperience.AtLevel(level)) return level;
        throw new NotSupportedException("Saved hero XP exceeds the compared original startup-power range.");
    }
}
