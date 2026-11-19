using System.Buffers.Binary;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold C0 605AAA..605C61 on the current restored local hero before
/// saved items exist. Runs the compared empty-list/auxiliary-model path and
/// real scalar/art calculation. Does not place, attach or authorize readiness.</summary>
public static class WorldCreatureHeroPostLoad
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, SacredCreatureTemplate template,
        WorldCreatureScalarResultAssets results, SacredCreatureArtTimerInputs definitions,
        SacredHostHeroPostLoadDefinitions rules, uint difficulty)
    {
        if (actor.HeroPostLoadCompleted) return actor;
        var saved = actor.RestoredCreature;
        if (saved is null || actor.RestoredPhysical is null || !actor.Node.Active ||
            actor.Base.ObjectReference is 0 or > 16 || actor.Base.Owner != actor.Base.ObjectReference ||
            template.Class != SacredCreatureClass.Hero || template.ItemType != actor.Base.ObjectType ||
            (actor.Base.Flags & (2 | 0x04000000)) != 0x04000000 || actor.PartnerReference != 0 ||
            actor.EquipmentReferences.Any(value => value != 0) || saved.StatusModifiers.Count != 0 || saved.HasExtraLists ||
            saved.Short(SacredSavedCreatureLayout.AuxiliaryPhysicalReferenceOffset) != 0 ||
            actor.Base.ObjectType == rules.ExtraActionResource)
            throw new NotSupportedException("C0 post-load requires its compared current modeled local hero, no placement, auxiliary object, mount or status/installed items.");
        var flags = (actor.Base.Flags & ~0x200010u) | 0x200u;
        var ki = actor.KiState.ToArray(); ki.AsSpan(0, 4).Clear();
        var ei = actor.EiState.ToArray(); ei.AsSpan(0, 4).Clear();
        // 0x111/subcommand 2 enters the real handler with cleared KI/EI and
        // the admitted local reference. Empty extra/status lists, no mount or
        // auxiliary model and the already active node retain their state.
        // 0x40 is called for BOTH 0x21 and 0x28 before the 0x54 status query.
        var arts = definitions.WithRecords(saved.CombatArts);
        var skills = new SacredCreatureSkillContext(false, false, false, false, false, false, 0,
            saved.PendingEquipmentReferences[SacredSavedCreatureLayout.RightHandEquipmentIndex] != 0, false);
        var context = new WorldCreatureScalarResultContext(actor.Base.ObjectType, actor.Base.ObjectReference, difficulty,
            false, BinaryPrimitives.ReadUInt32LittleEndian(actor.Component.Health.AsSpan(16)), flags, 0,
            actor.Component.CombatProfile, arts.Count, 0, false, skills, actor.Base.ObjectReference, results.StartupPrecision)
            { SavedHeroArts = arts };
        var calculated = results.Calculate(actor.Component.Values, template, context, []);
        var health = actor.Component.Health.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(health.AsSpan(16), calculated.Regeneration);
        return actor with
        {
            Base = actor.Base with { Flags = flags }, KiState = ki, EiState = ei, KiMode = 0, EiMode = 0,
            LocomotionAction = 0, Component = new(calculated.Values, health, actor.Component.CombatProfile, 1),
            RestoredCreature = saved.WithCombatArts(calculated.CombatArts ?? throw new InvalidDataException("C0 art calculation missing.")),
            // CalcResults -> 571080 first clears its physical 0x7800 bits;
            // the outer C0 cleanup then applies its independent final mask.
            RestoredPhysical = actor.RestoredPhysical with { Flags = (actor.RestoredPhysical.Flags & ~0x7800u) & 0x2ef07fffu },
            HeroPostLoadCompleted = true
        };
    }
}
