using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Initial CreateNPC 0x48504F..0x48506F. Resolves actual installed
/// own-value equipment, composes compared scalar/health results and performs
/// the network caller's result-validity write. Publication remains separate.</summary>
public static class WorldCreatureEquippedResults
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, SacredCreatureTemplate template,
        WorldCreatureScalarResultAssets results, SacredEquipmentGenerationData data,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> registry, uint difficulty, uint localReference,
        bool networking, uint players)
    {
        if (difficulty is not (1 or 2) || actor.KiMode != 0 || actor.EiMode != 0 || actor.PaletteMask != 0 ||
            (actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 ||
            actor.Base.ObjectType != template.ItemType || template.Class == SacredCreatureClass.Hero)
            throw new NotSupportedException("Equipped startup results require an ordinary initial unloaded NPC.");
        if (template.Class == SacredCreatureClass.Horse && actor.EquipmentReferences.Any(reference => reference != 0))
            throw new NotSupportedException("Horse equipment results require their native installation/slot comparison.");
        var equipment = new List<WorldCreatureScalarEquipment>();
        for (ushort slot = 0; slot < actor.EquipmentReferences.Length; slot++)
        {
            var reference = actor.EquipmentReferences[slot];
            if (reference == 0) continue;
            if (slot >= 18 || !registry.TryGetValue(reference, out var item) || !item.Generated ||
                item.Physical.AttachmentParent != actor.Base.ObjectReference || item.Physical.Link != actor.Base.ObjectReference)
                throw new NotSupportedException("Initial result equipment must resolve to its registered installed item.");
            if (!data.Templates.TryGetSharedTemplate(item.Physical.Base.ObjectType, out var shared))
                throw new InvalidDataException("Installed item has no original shared equipment definition.");
            var values = (byte[])item.Values.Clone();
            Sacred.Core.Pak.Weapon.SacredEquipmentDamageBounds.Normalize(values);
            var category = data.Templates.GetItem(item.Physical.Base.ObjectType).ModelDesc.Category;
            equipment.Add(new(slot, [new(values, category, shared.SetType)
            {
                HasArmorModifierQuery = true,
                ArmorModifier = data.Modifiers.Find(item.Physical.Base.ObjectType)?.Definition.Modifier
            }]));
        }
        var skills = SacredCreatureHandSkills.Resolve(Hand(12), Hand(13));
        var health = (byte[])actor.Component.Health.Clone();
        var calculated = results.Calculate(actor.Component.Values, template,
            new(template.ItemType, actor.Base.ObjectReference, difficulty, false,
                BinaryPrimitives.ReadUInt32LittleEndian(health.AsSpan(16)), actor.Base.Flags, 0,
                actor.Component.CombatProfile, 0, 0, false, skills, localReference, results.StartupPrecision), equipment);
        BinaryPrimitives.WriteUInt32LittleEndian(health.AsSpan(16), calculated.Regeneration);
        results.CalculateHealth(calculated.Values, health, new(actor.Base.Owner, localReference, difficulty, networking,
            players, actor.BehaviorFlags, actor.Flags2B7, 0, false, false, results.StartupPrecision)
            { ObjectType = template.ItemType, CreatureClass = template.Class });
        return actor with
        {
            Component = new(calculated.Values, health, actor.Component.CombatProfile, networking ? (byte)0 : (byte)1),
            RuntimeFlags = actor.RuntimeFlags | calculated.EquipmentFlagAdditions
        };

        SacredCreatureHandEquipment? Hand(int slot)
        {
            var reference = actor.EquipmentReferences[slot];
            if (reference == 0) return null;
            var item = registry[reference];
            var shared = data.Templates.Equipment[item.Physical.Base.ObjectType];
            return new(data.Templates.GetItem(item.Physical.Base.ObjectType).ModelDesc.Category,
                shared.UsageIdentifier, shared.InstanceValues.EquipmentTypeCode, item.Values[5]);
        }
    }
}
