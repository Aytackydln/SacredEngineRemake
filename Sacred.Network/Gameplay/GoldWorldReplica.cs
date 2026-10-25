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
    public GoldSessionState Session { get; } = new();
    public GoldWorldRegionReplica Regions { get; } = new();
    public GoldScriptVariableReplica ScriptVariables { get; } = new();
    public GoldScriptResourceReplica ScriptResources { get; } = new();
    public GoldScriptDialogReplica ScriptDialogs { get; } = new();
    public GoldNpcIdleAnimationQueue NpcIdleAnimations { get; } = new();
    public GoldDoorInteractionReplica DoorInteractions { get; } = new();
    public GoldContainerInteractionReplica ContainerInteractions { get; } = new();
    public GoldWorldTriggerReplica WorldTriggers { get; } = new();
    public GoldMovementTriggerReplica MovementTriggers { get; } = new();
    public GoldMovementTriggerDeletionQueue MovementTriggerDeletions => MovementTriggers.PendingDeletions;
    private readonly Dictionary<(byte Sender, byte Origin, uint Id), SacredSavedInventory> _inventories = [];
    private readonly Dictionary<uint, SacredSavedInventory> _currentInventories = [];
    public SacredSavedInventory? FindInventory(uint id) => CreatureCommands.Interactions.InventoryRecords.Find(id, _currentInventories.GetValueOrDefault(id));
    public GoldWorldReplica()
    {
        SpecialFx = new(_objects, () => Revision++);
        Session.Lobby.Control.Settings.BindWorld(this);
        GoldWorldServices.Connect(this);
        Objects = new ReadOnlyDictionary<uint, GoldReplicatedObject>(_objects);
        Inventories = new ReadOnlyDictionary<(byte Sender, byte Origin, uint Id), SacredSavedInventory>(_inventories);
    }
    public IReadOnlyDictionary<uint, GoldReplicatedObject> Objects { get; }
    public IReadOnlyDictionary<ushort, GoldSectorEntrySlot> Sectors => SectorInterest.Confirmed;
    public IReadOnlyDictionary<(byte Sender, byte Origin, uint Id), SacredSavedInventory> Inventories { get; }
    public byte LocalPlayerSlot { get; private set; }
    /// <summary>Original local character inputs omitted from its compact upload (not derived results).</summary>
    public SacredSavedCreatureValues? LocalCharacterValues { get; private set; }
    public GoldOwnedCreatureThinkInputs OwnedCreatureThinking { get; } = new();
    public long Revision { get; private set; }
    public long SessionVersion { get; private set; }
    public GoldCombatReplica Combat { get; } = new();
    public GoldCreatureControlReplica CreatureControls { get; } = new();
    public GoldBowProjectileReplica BowProjectiles { get; } = new();
    public GoldAttachedFxReplica AttachedFx { get; } = new();
    public GoldObjectPresentationReplica ObjectPresentation { get; } = new();
    public GoldSpecialFxReplica SpecialFx { get; }
    public GoldCreatureReceiveActions CreatureActions { get; } = new();
    public GoldCreatureCommandsReplica CreatureCommands { get; } = new();
    public GoldReceiveWorkReplica ReceiveWork { get; } = new();
    public GoldConsumerDelivery Consumers { get; } = new();
    public bool CompleteObjectAnimation(GoldObjectPresentationWork work, uint motion)
    {
        if (!GoldObjectPresentationReplica.Complete(work, motion, _objects)) return false;
        var reference = work.Object.BaseState.ObjectReference;
        if (WorldTriggers.Records.TryGetValue(work.Object.BaseState.TriggerReference, out var trigger))
            _objects[reference] = _objects[reference] with { ActivationTriggerState = trigger.State };
        Revision++; return true;
    }
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
    public SacredCreatureClass? CreatureClassForType(uint type) =>
        _creatureClasses.TryGetValue(type, out var kind) ? kind : null;
    public void ConfigureObjectResources(IReadOnlyDictionary<uint, uint> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (_objects.Count != 0) throw new InvalidOperationException("Configure object resources before a world snapshot arrives.");
        _objectResources = resources.ToFrozenDictionary();
        WorldBanks.Counters.Messages.ConfigureItemResources(resources);
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
        WorldBanks.Counters.ConfigureCategories(categories);
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
        Session.Teams.SetLocalPlayer(upload.PlayerSlot);
        LocalCharacterValues = upload.CharacterValues;
        OwnedCreatureThinking.Seed(upload, Objects);
        SessionVersion++;
        Revision++;
    }
    public void Apply(GoldGameEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var before = Revision;
        if (entry is GoldObjectRemovalEvent projectileRemoval && BowProjectiles.Remove(projectileRemoval.ObjectReference)) Revision++;
        if (entry is GoldObjectEvent replacement) BowProjectiles.Remove(replacement.ObjectReference);
        switch (entry)
        {
            case GoldReceiveWorkEvent work: if (ReceiveWork.Apply(work, this, _objects)) Revision++; break;
            case GoldCreatureCommandEvent command:
                if (CreatureCommands.Apply(command, this, _objects)) Revision++;
                break;
            case GoldScriptInvokeEvent invocation: WorldBanks.Invocations.Apply(invocation, WorldBanks.Functions); Revision++; break;
            case GoldRegionCountdownEvent countdown: Session.ApplyControl(countdown); Revision++; break;
            case GoldCreatureReceiveActionEvent action:
                if (CreatureActions.Apply(action, _objects, _objectCategories)) Revision++;
                break;
            case GoldCounterEvent counter:
                WorldBanks.Counters.Apply(counter, WorldBanks.Functions, Objects, LocalPlayerSlot != 0 ? LocalPlayerSlot : Session.Teams.LocalPlayerSlot);
                Revision++; break;
            case GoldSpecialFxEvent specialFx:
                if (SpecialFx.Apply(specialFx)) Revision++;
                break;
            case GoldQuestEvent questDelta:
                WorldBanks.Quests.Apply(questDelta, this, _objects); Revision++; break;
            case GoldObjectAnimationEvent or GoldContainerCloseEvent:
                if (ObjectPresentation.Apply(entry, _objects)) Revision++;
                break;
            case GoldSessionNoticeEvent notice when notice.EndsSession:
                var sessionEnd = GoldSessionNoticeCodec.Decode(notice.Message);
                GoldSessionEndReducer.Apply(_objects, LocalPlayerSlot != 0 ? LocalPlayerSlot : Session.Teams.LocalPlayerSlot, BowProjectiles, sessionEnd);
                Session.ApplyControl(sessionEnd); Revision++; break;
            case GoldInactiveReceiveHookEvent ignored:
                _ = GoldInactiveReceiveHookCodec.Decode(ignored.Message); break;
            case GoldSessionLockEvent or GoldSessionGameFlagsEvent or GoldSessionPlayerLimitEvent or GoldTeamEvent or GoldChatEvent or GoldChannelChatEvent
                or GoldAdmissionEvent or GoldSessionPauseEvent or GoldCheatsEnabledEvent or GoldClientHostRoleEvent or GoldPlayerRosterEvent or GoldSessionNoticeEvent or GoldLobbyEvent or GoldSessionReceiveControlEvent:
                Session.ApplyControl(entry); Revision++; break;
            case GoldScriptWorldEvent script:
                WorldBanks.Apply(script, SectorWork.Runtime); Revision++;
                break;
            case GoldObjectQuestEvent quest:
                if (GoldObjectQuestReducer.Apply(_objects, quest)) Revision++;
                break;
            case GoldCreatureDialogEvent or GoldCreatureFightModeEvent:
                if (CreatureControls.Apply(entry, _objects)) Revision++;
                break;
            case GoldAttachedFxEvent attached:
                if (AttachedFx.Add(attached, _objects)) Revision++;
                break;
            case GoldObjectFxSignalEvent signal:
                signal = GoldObjectFxSignalCodec.Decode(signal.Message);
                if (BowProjectiles.Notify(signal.ObjectReference, signal.Notification)) Revision++;
                if (GoldObjectFxSignalReducer.Apply(_objects, signal)) Revision++;
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
                CreatureCommands.Dialogs.ApplyAttachments(ScriptDialogs, _objects);
                DeathScripts.Apply(bank.Snapshot);
                WorldTriggers.Apply(bank.Snapshot);
                Revision++;
                break;
            case GoldWorldRegionFlagsEvent or GoldWorldRegionValueEvent:
                Regions.Apply(entry);
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
            case GoldMovementTriggerInsertionEvent insertion:
                MovementTriggers.Apply(insertion);
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
            case GoldDeathScriptEvent or GoldObjectCallbackCleanupEvent:
                DeathScripts.Apply(entry); Revision++; break;
            case GoldCombatEvent combat:
                if (GoldCombatReducer.Apply(_objects, Combat, combat))
                {
                    if (combat is GoldCreatureAttackEvent) BowProjectiles.Add(_objects[combat.ObjectReference]);
                    Revision++;
                }
                break;
            case GoldObjectEvent obj:
                _objects.TryGetValue(obj.BaseState.ObjectReference, out var previous);
                _objects[obj.BaseState.ObjectReference] = GoldReplicatedObjectFactory.FromEvent(obj, previous,
                    _objectCategories.GetValueOrDefault(obj.BaseState.ObjectType), Revision + 1);
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
                    ReportedHitPoints = values.Values.CurrentHitPoints,
                    Death = values.Values.CurrentHitPoints > 0 ? null : valued.Death };
                Revision++;
                break;
            case GoldObjectPositionEvent relocated:
                if (GoldObjectPositionReducer.Apply(_objects, relocated)) Revision++;
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
        Consumers.Diagnostics.Observe(entry, Revision != before);
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
        GoldWorldServices.Clear(this);
        _objects.Clear(); _inventories.Clear();
        _currentInventories.Clear();
        LocalPlayerSlot = 0; LocalCharacterValues = null; SessionVersion++; Revision++;
    }
}
