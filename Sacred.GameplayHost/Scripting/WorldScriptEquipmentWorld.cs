using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x481D80 EquipItem's compared initial Horse and target-empty Armor paths.
/// Native publishes the untouched item before the Horse's nonzero-slot rejection.
/// Retains the uninstalled object and its private stream; other callers fail explicitly.</summary>
public sealed class WorldScriptEquipmentWorld(WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets,
    WorldScriptCreatureRuntime runtime, Action<string> log)
{
    private readonly List<GoldMessage> _publications = [];
    public IReadOnlyList<GoldMessage> Publications => _publications.AsReadOnly();
    public void Equip(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        var operands = SacredScriptOperandReader.Read(command);
        if (operands.Count < 2 || operands[0].Kind != SacredScriptArgumentKind.NullTerminatedString ||
            operands.Skip(1).Any(value => value.Kind != SacredScriptArgumentKind.TypeId))
            throw new NotSupportedException("EquipItem requires its compared target name and explicit item types.");
        var resource = WorldScriptCreatureResourceNames.Resolve(operands[0].Text, world.Resources, assets.Equipment?.Naming?.Resources);
        if (!context.IsExecutingStartup || !context.Multiplayer || !world.Resources.ObjectBindings.TryGetValue(resource, out var reference) ||
            !world.Factory.Actors.TryGetValue(reference, out var actor) ||
            actor.Base.Owner != runtime.Owner || (actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 ||
            actor.Node.Active || (actor.RuntimeFlags & 0x2000) != 0)
            throw new NotSupportedException("EquipItem requires the compared named dormant owned unloaded startup state.");
        var descriptors = operands.Skip(1).Select(value => assets.Objects.GetItem(unchecked((uint)value.Integer!.Value))).ToArray();
        if (actor.RelationClass != (uint)SacredCreatureClass.Horse &&
            descriptors.All(value => value.ModelDesc.Category is SacredItemCategory.Helmet or SacredItemCategory.ChestArmor or
                SacredItemCategory.ShoulderArmor or SacredItemCategory.FootArmor or SacredItemCategory.ArmArmor or SacredItemCategory.Wings or SacredItemCategory.Belt))
        {
            var equipment = assets.Equipment ?? throw new InvalidDataException("EquipItem needs original equipment definitions.");
            var slots = assets.EquipmentSlots ?? throw new InvalidDataException("EquipItem needs original installation slot definitions.");
            foreach (var descriptor in descriptors)
            {
                var result = WorldScriptArmorEquipmentCommand.Apply(actor,descriptor,world.EquipmentFactory,equipment,slots,
                    assets.Progression,world.Random,runtime.Difficulty,runtime.Owner,runtime.Clock()/1000d,
                    new(assets.Creatures.ResolveTemplate(assets.Objects.GetItem(actor.Base.ObjectType))
                        ?? throw new InvalidDataException("EquipNPC actor has no Creature.pak template."),
                        assets.Results, runtime.Owner, runtime.Players));
                _publications.AddRange(result.Publications);
                actor = result.Actor;
                world.UpdateScriptActor(actor);
                log($"Startup EquipItem Armor: actor {reference}, item {result.Item.Physical.Base.ObjectReference}, type {descriptor.ItemIndex}; pre-install publication private, startup installation send suppressed.");
            }
            return;
        }
        if (actor.RelationClass != (uint)SacredCreatureClass.Horse || actor.EquipmentReferences.Any(value => value != 0))
            throw new NotSupportedException("EquipItem requires its compared named dormant Horse or target-empty Armor state.");
        if (descriptors.Any(value => value.ModelDesc.Category != SacredItemCategory.HorseEquipment))
            throw new NotSupportedException("EquipItem needs the original generation/installation/publication for this equipment category.");
        var data = assets.Equipment ?? throw new InvalidDataException("EquipItem requires original equipment definitions.");
        foreach (var descriptor in descriptors)
        {
            var selected = WorldCreatureEquipmentSelection.Resolve(descriptor.ModelDesc.Category, null, false,
                reference, actor.EquipmentReferences);
            // Verify the bounded installer path before allocating; normal/mount installation is separate.
            if (selected == SacredCreatureEquipmentSlot.Unavailable || (ushort)selected is 0 or 18)
                throw new NotSupportedException("EquipItem requires its native accepted-slot installation effects.");
            var item = world.EquipmentFactory.Create(descriptor, assets.Objects.GetInstance(descriptor.ItemIndex), runtime.Clock()/1000d, context.Multiplayer);
            // Native 0x5D5080 returns before generation for category 29: keep original values/RNG/name.
            var physical = GoldPhysicalConstructionWriter.Write(item.Physical.Base, item.Physical.Direction);
            var state = GoldItemState.Create(physical, [], item.Values, item.InventoryId, []);
            item.SerializedState = state;
            item.Physical.Base = item.Physical.Base with { Flags = item.Physical.Base.Flags & ~0x10000u };
            _publications.Add(GoldObjectStateCodec.Create(0x16e, runtime.Owner, state.ToPayload()));
            var installation = new WorldCreatureEquipmentState(actor.Base, true, actor.EquipmentReferences) { Behavior = actor.BehaviorFlags };
            var effects = new WorldCreatureInitialEquipmentEffects(data, world.EquipmentFactory.Items, actor);
            new WorldCreatureEquipmentInstallation(effects, context.Multiplayer).Install(installation, (ushort)selected, item.Physical.Base.ObjectReference, false);
            log($"Startup EquipItem: actor {reference}, item {item.Physical.Base.ObjectReference}, type {descriptor.ItemIndex}, selected slot {(ushort)selected}; native Horse installer retained the uninstalled item; publication private.");
        }
    }
}
