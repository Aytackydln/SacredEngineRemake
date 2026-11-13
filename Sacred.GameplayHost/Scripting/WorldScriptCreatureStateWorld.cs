using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
namespace Sacred.GameplayHost.Scripting;

/// <summary>Native SetNPCState's compared startup owner/level/mask-8 paths.</summary>
public sealed class WorldScriptCreatureStateWorld(WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets,
    WorldScriptCreatureRuntime runtime, Action<string> log)
{
    private readonly List<GoldMessage> _publications = [];
    public IReadOnlyList<GoldMessage> Publications => _publications.AsReadOnly();
    public void SetState(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        var operands = SacredScriptOperandReader.Read(command);
        var levelOnly=operands.Count==2 && operands[1].Kind==SacredScriptArgumentKind.CreatureLevel;
        var flagOnly=operands.Count==2 && operands[1].Kind==SacredScriptArgumentKind.CreatureFlag2B7Mask08;
        var dialogOnly=operands.Count==2 && operands[1].Kind is SacredScriptArgumentKind.DialogRegistrationName or SacredScriptArgumentKind.DialogRegistrationNumber;
        if (operands.Count is < 2 or > 3 || operands[0].Kind != SacredScriptArgumentKind.NullTerminatedString ||
            (!levelOnly && !flagOnly && !dialogOnly && operands.Skip(1).Count(value => value.Kind == SacredScriptArgumentKind.CreatureOwner) != 1) ||
            (!flagOnly && !dialogOnly && operands.Skip(1).Any(value => value.Kind is not (SacredScriptArgumentKind.CreatureOwner or SacredScriptArgumentKind.CreatureLevel))) || !context.IsExecutingStartup || !context.Multiplayer)
            throw new NotSupportedException("SetNPCState requires its compared startup target and owner operands.");
        WorldCreatureInitialActor Actor(string? name)
        {
            var resource = WorldScriptCreatureResourceNames.Resolve(name,world.Resources,assets.Equipment?.Naming?.Resources);
            if (!world.Resources.ObjectBindings.TryGetValue(resource,out var reference) || !world.Factory.Actors.TryGetValue(reference,out var actor))
                throw new NotSupportedException("SetNPCState requires an existing native resource-to-creature binding.");
            if (actor.Base.Owner != runtime.Owner) throw new NotSupportedException("SetNPCState requires native authority transfer before changing this owner.");
            return actor;
        }
        var target = Actor(operands[0].Text);
        if (dialogOnly)
        {
            var operand = operands[1];
            var selection = new SacredScriptDialogSelection(operand.Text,
                operand.Kind == SacredScriptArgumentKind.DialogRegistrationNumber ? checked((ushort)operand.Integer!.Value) : null);
            target = WorldCreatureScriptDialogState.Apply(target, context.Registrations, selection);
            world.UpdateScriptActor(target);
            var current = world.Prepared.Single(value => value.Actor?.Base.ObjectReference == target.Base.ObjectReference).CreatureState!;
            _publications.Add(GoldCreatureUpdateCodec.CreateState(runtime.Owner, target.Base.ObjectReference,
                target.Base.ObjectType, GoldCreatureUpdateState.FromCreature(current)));
            var dialogTemplate = assets.Creatures.Entries.Single(value => value.ItemType == target.Base.ObjectType);
            target = WorldCreatureEquippedResults.Apply(target, dialogTemplate, assets.Results,
                assets.Equipment ?? throw new InvalidDataException("SetNPCState dialog requires original result definitions."),
                world.EquipmentFactory.Items, runtime.Difficulty, runtime.Owner, context.Multiplayer, runtime.Players);
            target = target with { Component = target.Component with { ResultValid = 1 } };
            world.UpdateScriptActor(target);
            log($"Startup SetNPCState dialog: ref {target.Base.ObjectReference}, slot {target.DialogIndex}; registration and results private, 0x11A retained.");
            return;
        }
        if (flagOnly)
        {
            target=WorldCreatureScriptFlagState.Apply(target,operands[1].Integer!.Value);
            var flagTemplate=assets.Creatures.Entries.Single(value=>value.ItemType==target.Base.ObjectType);
            target=WorldCreatureEquippedResults.Apply(target,flagTemplate,assets.Results,
                assets.Equipment ?? throw new InvalidDataException("SetNPCState requires original result definitions."),
                world.EquipmentFactory.Items,runtime.Difficulty,runtime.Owner,context.Multiplayer,runtime.Players);
            target=target with { Component=target.Component with { ResultValid=1 } };
            world.UpdateScriptActor(target);
            log($"Startup SetNPCState mask 8: ref {target.Base.ObjectReference}, selector {operands[1].Integer}; results private, no update message.");
            return;
        }
        var levelOperand = operands.FirstOrDefault(value => value.Kind == SacredScriptArgumentKind.CreatureLevel);
        if (levelOperand.Kind == SacredScriptArgumentKind.CreatureLevel)
        {
            var level = levelOperand.Integer ?? throw new NotSupportedException("SetNPCState needs original level expression evaluation.");
            if (level == 1000) throw new NotSupportedException("SetNPCState special-level selection requires its complete state-command comparison.");
            target = WorldCreatureScriptLevel.ApplyRequested(target,assets.Creatures.Entries.Single(value => value.ItemType == target.Base.ObjectType),level,
                assets.Results,assets.Levels,assets.Progression,assets.LevelDefinitions,runtime.Difficulty,runtime.Owner,context.Multiplayer,0,runtime.Players);
        }
        var template = assets.Creatures.Entries.Single(value => value.ItemType == target.Base.ObjectType);
        if (levelOnly)
        {
            target=WorldCreatureEquippedResults.Apply(target,template,assets.Results,
                assets.Equipment ?? throw new InvalidDataException("SetNPCState requires original result definitions."),
                world.EquipmentFactory.Items,runtime.Difficulty,runtime.Owner,context.Multiplayer,runtime.Players);
            target=target with { Component=target.Component with { ResultValid=1 } };
            world.UpdateScriptActor(target);
            // No dialog setter runs; 0x114 is suppressed during startup. Existing
            // Horse price and reciprocal partner remain untouched by this command.
            log($"Startup SetNPCState level: ref {target.Base.ObjectReference}, requested {levelOperand.Integer}; final results private, no update message.");
            return;
        }
        var owner = Actor(operands.Single(value => value.Kind == SacredScriptArgumentKind.CreatureOwner).Text);
        var values = assets.HorseValues ?? throw new InvalidDataException("Original Horse value curves unavailable.");
        var definitions = assets.HorseOwner ?? throw new InvalidDataException("Original Horse owner dialog definitions unavailable.");
        WorldCreatureInitialActor horse;
        if (owner.PartnerReference != 0)
        {
            if (!world.Factory.Actors.TryGetValue(owner.PartnerReference,out var previous))
                throw new NotSupportedException("SetOwner prior Horse must resolve before its native detach lifecycle.");
            (horse,owner,previous) = WorldHorseOwnerAssignment.ApplyReplacing(target,owner,previous,values,definitions,assets.Results.StartupPrecision);
            world.UpdateScriptActor(previous);
            _publications.Add(GoldCreatureMountCodec.Create(runtime.Owner,0,previous.Base.ObjectReference));
        }
        else (horse,owner) = WorldHorseOwnerAssignment.Apply(target,owner,values,definitions,assets.Results.StartupPrecision);
        horse = WorldCreatureEquippedResults.Apply(horse,template,assets.Results,
            assets.Equipment ?? throw new InvalidDataException("SetNPCState requires original result definitions."),
            world.EquipmentFactory.Items,runtime.Difficulty,runtime.Owner,context.Multiplayer,runtime.Players);
        horse = horse with { Component = horse.Component with { ResultValid = 1 } };
        world.UpdateScriptActor(horse);world.UpdateScriptActor(owner);
        // Native dialog setter explicitly requests 0x11A even in startup; 0x114 stays suppressed.
        var creature = world.Prepared.Single(value => value.Actor?.Base.ObjectReference == horse.Base.ObjectReference).CreatureState!;
        _publications.Add(GoldCreatureUpdateCodec.CreateState(runtime.Owner,horse.Base.ObjectReference,horse.Base.ObjectType,
            GoldCreatureUpdateState.FromCreature(creature)));
        log($"Startup SetOwner: Horse {horse.Base.ObjectReference}, partner {owner.Base.ObjectReference}, dialog {horse.DialogIndex}; reciprocal link and original value applied; 0x11A private.");
    }
}
