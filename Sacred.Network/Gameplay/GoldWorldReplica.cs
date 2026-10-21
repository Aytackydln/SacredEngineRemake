using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Main-thread replica state shared by graphical and headless world consumers. It does not calculate item values.</summary>
public sealed class GoldWorldReplica
{
    private readonly Dictionary<uint, GoldReplicatedObject> _objects = [];
    private FrozenDictionary<uint, SacredCreatureClass> _creatureClasses = FrozenDictionary<uint, SacredCreatureClass>.Empty;
    private FrozenDictionary<uint, uint> _objectResources = FrozenDictionary<uint, uint>.Empty;
    private FrozenDictionary<uint, SacredItemCategory> _objectCategories = FrozenDictionary<uint, SacredItemCategory>.Empty;
    public GoldSectorInterestState SectorInterest { get; } = new();
    public GoldSectorWorkQueue SectorWork { get; } = new();
    public GoldTriggerReferencePool TriggerReferences { get; } = new();
    public GoldDeathScriptRegistry DeathScripts { get; } = new();
    public GoldPlayerStatisticsReplica Statistics { get; } = new();
    public GoldCalendarReplica Calendar { get; } = new();
    public GoldWeatherReplica Weather { get; } = new();
    public GoldWorldBankReplica WorldBanks { get; } = new();
    public GoldWorldRegionReplica Regions { get; } = new();
    public GoldScriptVariableReplica ScriptVariables { get; } = new();
    public GoldScriptResourceReplica ScriptResources { get; } = new();
    public GoldScriptDialogReplica ScriptDialogs { get; } = new();
    public GoldNpcIdleAnimationQueue NpcIdleAnimations { get; } = new();
    public GoldDoorInteractionReplica DoorInteractions { get; } = new();
    public GoldContainerInteractionReplica ContainerInteractions { get; } = new();
    public GoldWorldTriggerReplica WorldTriggers { get; } = new();
    public GoldMovementTriggerReplica MovementTriggers { get; } = new();
    public GoldMovementTriggerDeletionQueue MovementTriggerDeletions { get; } = new();
    private readonly Dictionary<(byte Sender, byte Origin, uint Id), SacredSavedInventory> _inventories = [];
    private readonly Dictionary<uint, SacredSavedInventory> _currentInventories = [];
    public SacredSavedInventory? FindInventory(uint id) => _currentInventories.GetValueOrDefault(id);
    public GoldWorldReplica()
    {
        Objects = new ReadOnlyDictionary<uint, GoldReplicatedObject>(_objects);
        Inventories = new ReadOnlyDictionary<(byte Sender, byte Origin, uint Id), SacredSavedInventory>(_inventories);
    }
    public IReadOnlyDictionary<uint, GoldReplicatedObject> Objects { get; }
    public IReadOnlyDictionary<ushort, GoldSectorEntrySlot> Sectors => SectorInterest.Confirmed;
    public IReadOnlyDictionary<(byte Sender, byte Origin, uint Id), SacredSavedInventory> Inventories { get; }
    public byte LocalPlayerSlot { get; private set; }
    /// <summary>Original local character inputs omitted from its compact upload (not derived results).</summary>
    public SacredSavedCreatureValues? LocalCharacterValues { get; private set; }
    public long Revision { get; private set; }
    public long SessionVersion { get; private set; }
    public GoldCombatReplica Combat { get; } = new();
    public GoldBowProjectileReplica BowProjectiles { get; } = new();
    public GoldAttachedFxReplica AttachedFx { get; } = new();

    /// <summary>Native interest scheduling also updates local sector receive flags before any reply arrives.</summary>
    public void BeginSectorInterest(GoldSectorInterestRequest request)
    {
        SectorInterest.Begin(request);
        SectorWork.Runtime.Begin(request);
    }

    /// <summary>Supply original installation definitions before object arrival; never infer horse class from resource IDs.</summary>
    public void ConfigureCreatureClasses(IReadOnlyDictionary<uint, SacredCreatureClass> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        if (_objects.Count != 0) throw new InvalidOperationException("Configure creature classes before a world snapshot arrives.");
        _creatureClasses = classes.ToFrozenDictionary();
    }

    public void ConfigureObjectResources(IReadOnlyDictionary<uint, uint> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (_objects.Count != 0) throw new InvalidOperationException("Configure object resources before a world snapshot arrives.");
        _objectResources = resources.ToFrozenDictionary();
    }

    public uint? FindScriptObject(uint resource) => ScriptResources.FindObject(resource, Objects,
        type => _objectResources.TryGetValue(type, out var value) ? value : 0);

    /// <summary>EnterSector's local post-publication ambient initialization at
    /// 0x4945A8..0x4945FC. The earlier creation packet retains its original fields.</summary>
    public void InitializeOwnedCreatureDirection(uint reference,uint addedFlags,GoldPhysicalVector direction)
    {
        if(LocalPlayerSlot==0 || !_objects.TryGetValue(reference,out var obj) || obj.BaseState.Owner!=LocalPlayerSlot ||
            obj.Creature is null || obj.Physical is null) throw new InvalidOperationException("Ambient initialization requires a locally owned creature.");
        var basis=obj.BaseState with{Flags=obj.BaseState.Flags|addedFlags};
        var payload=obj.Creature.ToPayload();
        var physical=obj.Physical.WithDirection(direction).ToPayload();
        basis.ToPayload().CopyTo(physical,0);
        physical.CopyTo(payload,0);
        var creature=GoldCreatureState.FromPayload(payload);
        _objects[reference]=obj with{BaseState=basis,Creature=creature,Physical=creature.PhysicalState,FacingBits=GoldPhysicalFacing.FromDirection(direction)};
        Revision++;
    }

    public void ConfigureObjectCategories(IReadOnlyDictionary<uint, SacredItemCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (_objects.Count != 0) throw new InvalidOperationException("Configure object categories before a world snapshot arrives.");
        _objectCategories = categories.ToFrozenDictionary();
    }

    public void SeedCharacter(GoldParticipantCharacterUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (LocalPlayerSlot != 0) throw new InvalidOperationException("Character replica already seeded; reset before another session.");
        var events = upload.CreateMessages().Select(GoldGameEventDecoder.Decode).ToArray();
        // The bundle has already validated every owned reference and complete layer before any state is seeded.
        foreach (var entry in events)
        {
            if (entry is GoldObjectEvent obj && _objects.TryGetValue(obj.BaseState.ObjectReference, out var current))
            {
                if (current.BaseState.ObjectType == obj.BaseState.ObjectType)
                    _objects[obj.BaseState.ObjectReference] = current with { Creature = current.Creature ?? obj.CreatureState,
                        Item = current.Item ?? obj.ItemState,
                        ItemValuesVersion = current.Item is null && obj.ItemState is not null ? Revision + 1 : current.ItemValuesVersion,
                        Values = current.Values ?? obj.CreatureTail?.Values,
                        MountPartnerReference = current.MountPartnerReference ?? obj.CreatureTail?.MountPartnerReference };
            }
            else Apply(entry);
        }
        LocalPlayerSlot = upload.PlayerSlot;
        LocalCharacterValues = upload.CharacterValues;
        SessionVersion++;
        Revision++;
    }

    public void Apply(GoldGameEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry is GoldObjectRemovalEvent projectileRemoval && BowProjectiles.Remove(projectileRemoval.ObjectReference)) Revision++;
        if (entry is GoldObjectEvent replacement) BowProjectiles.Remove(replacement.ObjectReference);
        switch (entry)
        {
            case GoldAttachedFxEvent attached:
                if (AttachedFx.Add(attached, _objects)) Revision++;
                break;
            case GoldObjectFxSignalEvent signal:
                if (BowProjectiles.Notify(signal.ObjectReference, signal.Notification)) Revision++;
                break;
            case GoldObjectFlightEvent flight:
                if (GoldObjectFlightReducer.Apply(_objects, flight)) Revision++;
                break;
            case GoldItemPickupEvent taken when taken.Message.Opcode == GoldItemPickupCodec.TakenOpcode
                && _objects.TryGetValue(taken.ObjectReference, out var ground):
                _objects[taken.ObjectReference] = ground with { BaseState = ground.BaseState with
                    { X = 0, Y = 0, Surface = 0, Sector = 0 }, Flight = null, FlightVersion = ground.FlightVersion + 1 };
                Revision++;
                break;
            case GoldContainerActivationEvent container:
                ContainerInteractions.Apply(container, _objects, WorldTriggers);
                Revision++;
                break;
            case GoldDoorEvent door:
                DoorInteractions.Apply(door, _objects, WorldTriggers);
                Revision++;
                break;
            case GoldNpcIdleAnimationEvent npcIdle:
                NpcIdleAnimations.Add(npcIdle with { ReplicaContext = new(_objects.GetValueOrDefault(npcIdle.InitiatorReference),
                    _objects.GetValueOrDefault(npcIdle.TargetReference)) });
                Revision++;
                break;
            case GoldWeatherEvent weather:
                Weather.Apply(weather);
                Revision++;
                break;
            case GoldWorldEnvironmentEvent environment:
                Calendar.Apply(environment);
                Weather.Apply(environment);
                Revision++;
                break;
            case GoldObjectRemovalEvent removal when removal.ObjectReference != LocalPlayerSlot:
            case GoldObjectOwnerEvent:
                if (GoldObjectLifecycleReducer.Apply(_objects, entry)) Revision++;
                break;
            case GoldReferenceRangeEvent triggerReferences:
                TriggerReferences.Apply(triggerReferences);
                Revision++;
                break;
            case GoldWorldBankEvent bank:
                WorldBanks.Apply(bank);
                Regions.Apply(bank.Snapshot);
                MovementTriggers.Apply(bank.Snapshot);
                ScriptVariables.Apply(bank.Snapshot);
                ScriptResources.Apply(bank.Snapshot);
                ScriptDialogs.Apply(bank.Snapshot);
                WorldTriggers.Apply(bank.Snapshot);
                Revision++;
                break;
            case GoldWorldRegionFlagsEvent region:
                Regions.Apply(region);
                Revision++;
                break;
            case GoldScriptVariableEvent variable:
                ScriptVariables.Apply(variable);
                Revision++;
                break;
            case GoldScriptResourceEvent resource:
                if (ScriptResources.Apply(resource) is { } link) WorldTriggers.SetReferent(link.Trigger, link.Resource);
                Revision++;
                break;
            case GoldWorldTriggerEvent worldTrigger:
                if (worldTrigger.Message.Opcode == GoldWorldTriggerCodec.CreateOpcode)
                {
                    if (WorldTriggers.TryCreate(worldTrigger)) ScriptResources.RegisterTrigger(worldTrigger.Name!, worldTrigger.Reference);
                }
                else WorldTriggers.Apply(worldTrigger);
                Revision++;
                break;
            case GoldScriptDialogEvent dialog:
                ScriptDialogs.Apply(dialog);
                Revision++;
                break;
            case GoldEquipmentRemovalEvent removal when _objects.TryGetValue(removal.CreatureReference, out var equipped):
                if (GoldEquipmentRemovalReducer.Apply(equipped, removal) is { } changed)
                {
                    _objects[removal.CreatureReference] = changed;
                    Revision++;
                }
                break;
            case GoldEquipmentAssignmentEvent assignment:
                if (GoldEquipmentAssignmentReducer.Apply(_objects, assignment,
                    resource => _creatureClasses.TryGetValue(resource, out var kind) && kind == SacredCreatureClass.Horse))
                    Revision++;
                break;
            case GoldCreatureMountEvent mount:
                if (GoldCreatureMountReducer.Apply(_objects, mount, resource => resource > 0 && resource < GoldCreatureMountLayout.ResourceLimit &&
                    _creatureClasses.TryGetValue(resource, out var kind) && kind == SacredCreatureClass.Horse))
                    Revision++;
                break;
            case GoldMovementTriggerDeletionEvent deletion:
                if (!MovementTriggers.Apply(deletion.Deletion)) MovementTriggerDeletions.Add(deletion.Deletion);
                Revision++;
                break;
            case GoldPlayerStatisticsEvent statistics:
                Statistics.Apply(statistics);
                Revision++;
                break;
            case GoldObjectTriggerEvent trigger when _objects.TryGetValue(trigger.ObjectReference, out var triggered)
                && (triggered.BaseState.Flags & 0x10) == 0:
                _objects[trigger.ObjectReference] = triggered with { BaseState = triggered.BaseState with { Word34 = trigger.TriggerReference } };
                Revision++;
                break;
            case GoldDeathScriptEvent script:
                DeathScripts.Register(script.Registration);
                Revision++;
                break;
            case GoldCombatEvent combat:
                if (GoldCombatReducer.Apply(_objects, Combat, combat))
                {
                    if (combat is GoldCreatureAttackEvent) BowProjectiles.Add(_objects[combat.ObjectReference]);
                    Revision++;
                }
                break;
            case GoldObjectEvent obj:
                var basis = obj.BaseState;
                _objects.TryGetValue(basis.ObjectReference, out var previous);
                if (previous?.BaseState.ObjectType != basis.ObjectType ||
                    previous is not null && (previous.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) previous = null;
                _objects[basis.ObjectReference] = new(basis, obj.CreatureState ?? previous?.Creature,
                    obj.ItemState ?? previous?.Item, obj.CreatureState is not null ? null : previous?.Movement)
                { InstanceVersion = previous?.InstanceVersion ?? Revision + 1,
                    ItemValuesVersion = obj.ItemState is not null ? Revision + 1 : previous?.ItemValuesVersion ?? 0,
                    Container = obj.ItemState is not null && _objectCategories.GetValueOrDefault(basis.ObjectType) == SacredItemCategory.Container
                        ? GoldContainerState.FromPayload(obj.ObjectState) : previous?.Container,
                    Physical = obj.PhysicalState ?? previous?.Physical,
                    Fx = obj.FxState ?? previous?.Fx,
                    Values = obj.CreatureTail?.Values ?? previous?.Values,
                    CurrentHitPoints = obj.CreatureTail?.Values?.CurrentHitPoints ?? previous?.CurrentHitPoints,
                    ReportedHitPoints = obj.CreatureTail?.Values?.CurrentHitPoints ?? previous?.ReportedHitPoints,
                    Attack = obj.CreatureState is not null ? null : previous?.Attack,
                    SpecialMove = obj.CreatureState is not null ? null : previous?.SpecialMove,
                    Cast = obj.CreatureState is not null ? null : previous?.Cast,
                    AttackVersion = (previous?.AttackVersion ?? 0) + (obj.CreatureState is not null ? 1 : 0),
                    FacingBits = obj.PhysicalState is { } physicalState ? GoldPhysicalFacing.FromDirection(physicalState.Vector70) : basis.Word28,
                    PositionOpcode = obj.Message.Opcode,
                    FlightVersion = (previous?.FlightVersion ?? 0) + 1,
                    PositionVersion = (previous?.PositionVersion ?? 0) + 1,
                    MovementVersion = (previous?.MovementVersion ?? 0) + 1,
                    MountPartnerReference = obj.CreatureTail?.MountPartnerReference ?? previous?.MountPartnerReference };
                Revision++;
                break;
            case GoldCreatureUpdateEvent update when _objects.TryGetValue(update.ObjectReference, out var creature)
                && creature.Creature is not null && (creature.BaseState.Flags & 0x10) == 0:
                _objects[update.ObjectReference] = creature with { Creature = update.State.ApplyTo(creature.Creature) };
                Revision++;
                break;
            case GoldCreatureValuesEvent values when _objects.TryGetValue(values.ObjectReference, out var valued)
                && valued.Creature is not null && (valued.BaseState.Flags & 0x10) == 0:
                _objects[values.ObjectReference] = valued with { Values = values.Values, CurrentHitPoints = values.Values.CurrentHitPoints,
                    ReportedHitPoints = values.Values.CurrentHitPoints };
                Revision++;
                break;
            case GoldObjectPositionEvent relocated when _objects.TryGetValue(relocated.ObjectReference, out var physical)
                && physical.Physical is not null && (physical.BaseState.Flags & 0x10) == 0:
                var flightStopped = relocated.Message.Opcode == GoldObjectFlightStopCodec.Opcode;
                var relocatedBase = AtPosition(physical.BaseState, relocated.Position);
                _objects[relocated.ObjectReference] = physical with { BaseState = flightStopped
                    ? relocatedBase with { Flags = relocatedBase.Flags & ~GoldObjectFlightLayout.FlyingFlag } : relocatedBase,
                    Flight = flightStopped ? null : physical.Flight,
                    FlightVersion = physical.FlightVersion + (flightStopped ? 1 : 0),
                    Physical = physical.Physical.WithDirection(GoldPhysicalFacing.ToDirection(relocated.FacingBits)),
                    Attack = null, SpecialMove = null, Cast = null, AttackVersion = physical.AttackVersion + 1,
                    Movement = physical.Creature is not null ? null : physical.Movement, FacingBits = relocated.FacingBits,
                    PositionOpcode = relocated.Message.Opcode, PositionVersion = physical.PositionVersion + 1,
                    MovementVersion = physical.MovementVersion + 1 };
                Revision++;
                break;
            case GoldSectorEntryEvent reply:
                SectorInterest.Confirm(reply.Entry);
                SectorWork.Enqueue(reply.Entry, WorldBanks.Current);
                Revision++;
                break;
            case GoldMovementEvent movement when _objects.TryGetValue(movement.ObjectReference, out var actor)
                                                 && actor.Creature is not null && (actor.BaseState.Flags & 0x10) == 0:
                // Authoritative position stays at the sender's anchor. Shared path playback owns its private clock/copy.
                var position = movement.Body is not null ? movement.Position : (GoldObjectPosition?)null;
                _objects[movement.ObjectReference] = actor with { BaseState = position is { } anchor ?
                    AtPosition(actor.BaseState, anchor) : actor.BaseState, PositionOpcode = position is null ? actor.PositionOpcode : movement.Message.Opcode,
                    PositionVersion = actor.PositionVersion + (position is null ? 0 : 1), MovementVersion = actor.MovementVersion + 1,
                    Attack = null, SpecialMove = null, Cast = null, AttackVersion = actor.AttackVersion + 1,
                    Movement = movement with
                    { Message = movement.Message with { Payload = movement.Message.Payload.ToArray() }, ObjectState = movement.ObjectState.ToArray() } };
                Revision++;
                break;
            case GoldInventoryEvent inventory:
                _inventories[(inventory.Message.Sender, inventory.Message.Origin, inventory.InventoryId)] = inventory.Inventory;
                _currentInventories[inventory.InventoryId] = inventory.Inventory;
                Revision++;
                break;
            case GoldMovementEndEvent end when _objects.TryGetValue(end.ObjectReference, out var stopped)
                                               && stopped.Creature is not null && (stopped.BaseState.Flags & 0x10) == 0:
                _objects[end.ObjectReference] = stopped with { BaseState = AtPosition(stopped.BaseState, end.Position),
                    Attack = null, SpecialMove = null, Cast = null, AttackVersion = stopped.AttackVersion + 1,
                    Movement = null, FacingBits = end.FacingBits, PositionOpcode = end.Message.Opcode,
                    PositionVersion = stopped.PositionVersion + 1, MovementVersion = stopped.MovementVersion + 1 };
                Revision++;
                break;
            case GoldIdleEvent idle when _objects.TryGetValue(idle.ObjectReference, out var idleActor)
                && (idleActor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0:
                _objects[idle.ObjectReference] = idleActor with { Movement = null, MovementVersion = idleActor.MovementVersion + 1,
                    Attack = null, SpecialMove = null, Cast = null, AttackVersion = idleActor.AttackVersion + 1 };
                Revision++;
                break;
        }
        if (entry is GoldObjectRemovalEvent or GoldObjectEvent) AttachedFx.Prune(_objects);
    }

    private static GoldObjectBaseState AtPosition(GoldObjectBaseState state, GoldObjectPosition position) =>
        state with { X = position.X, Y = position.Y, Surface = unchecked((byte)position.Surface), Sector = unchecked((ushort)position.Sector) };

    /// <summary>Owned client prediction between native sends. Remote actors can only change through Apply.</summary>
    public void PredictLocalPosition(GoldObjectPosition position, uint facingBits)
    {
        if (LocalPlayerSlot == 0 || !_objects.TryGetValue(LocalPlayerSlot, out var hero) || hero.Creature is null ||
            hero.BaseState.Owner != LocalPlayerSlot || (hero.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 ||
            !Sectors.ContainsKey(unchecked((ushort)position.Sector))) throw new InvalidOperationException("Prediction requires the admitted hero and confirmed terrain.");
        _objects[LocalPlayerSlot] = hero with { BaseState = AtPosition(hero.BaseState, position), FacingBits = facingBits };
        Revision++;
    }

    public void Clear()
    {
        Combat.Clear();
        BowProjectiles.Clear();
        AttachedFx.Clear();
        _objects.Clear(); SectorInterest.Clear(); SectorWork.Clear(); DeathScripts.Clear(); Statistics.Clear(); _inventories.Clear();
        _currentInventories.Clear();
        MovementTriggerDeletions.Clear();
        WorldBanks.Clear();
        Regions.Clear();
        ScriptVariables.Clear();
        ScriptResources.Clear();
        ScriptDialogs.Clear();
        NpcIdleAnimations.Clear();
        DoorInteractions.Clear();
        ContainerInteractions.Clear();
        WorldTriggers.Clear();
        MovementTriggers.Clear();
        TriggerReferences.Clear();
        Calendar.Clear();
        Weather.Clear();
        LocalPlayerSlot = 0; LocalCharacterValues = null; SessionVersion++; Revision++;
    }
}
