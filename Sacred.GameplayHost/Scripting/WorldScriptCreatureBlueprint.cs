using Sacred.Assets.GameBin;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Resolved authored creature inputs. This is not an allocated/runtime actor;
/// native constructors, group registration, equipment installation and value calculation remain required.</summary>
public sealed record WorldScriptCreatureBlueprint(int SourceOffset, SacredScriptCreateCreature Creation,
    SacredCreatureTemplate Template, ItemsPakEntry Visuals, IReadOnlyList<ItemsPakEntry> Equipment)
{
    public static WorldScriptCreatureBlueprint Read(WorldScriptExecutionContext context, SacredScriptCommand command,
        SacredObjectTemplateArchive objects, CreaturePakArchive creatures)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(creatures);
        var creation = SacredScriptCreatureCreationReader.Read(command,
            name => context.Definitions.ResolvePosition(name, context.Random, context.PositionAllowed), context.Variables.Find);
        var item = objects.GetItem(creation.TypeId);
        var template = creatures.ResolveTemplate(item) ??
            throw new InvalidDataException($"Creature {creation.TypeId} has no resolved Creature.pak template.");
        if (template.RecordSize != CreatureGoldRecordLayout.Size)
            throw new InvalidDataException("Gold creature construction requires a Gold Creature.pak record.");
        return new(command.FileOffset, creation, template, item,
            Array.AsReadOnly(creation.EquipmentTypes.Select(objects.GetItem).ToArray()));
    }
}
