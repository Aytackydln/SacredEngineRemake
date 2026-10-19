using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Core.Pax.Hero;
using Sacred.Core.World;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Engine.Multiplayer;

/// <summary>Main-thread owner of the transport and verified gameplay dispatch.</summary>
internal sealed partial class GameServerClientController : IDisposable
{
    private GoldGameConnection? _connection;
    private GoldFirstContact? _firstContact;
    private readonly Dictionary<ushort, int> _opcodeCounts = [];
    private readonly Dictionary<uint, uint[]> _movements = [];
    private readonly Dictionary<uint, GoldObjectEvent> _objects = [];
    private readonly Dictionary<uint, bool> _doors = [];
    private readonly Dictionary<(byte Sender, byte Origin, uint Id), GoldInventoryEvent> _inventories = [];
    private readonly List<uint> _allocatedReferences = [];
    private readonly HashSet<uint> _referenceSet = [];
    public GoldObjectReferencePool ObjectReferences { get; } = new();
    private readonly IGameplayReplicaSession _gameplay = new SacredGameplayReplicaSession();
    public GoldWorldReplica WorldReplicas => _gameplay.Replicas;
    public int GrantedReferenceCount => _allocatedReferences.Count;
    public int EventsReceived => _eventsReceived;
    public uint LocalMovementControl { get; private set; }
    public IReadOnlyDictionary<ushort, int> OpcodeCounts => _opcodeCounts;
    public byte PlayerSlot => _connection?.Admission.PlayerSlot ?? 0;
    public bool IsConnected => _connection is { Completion.IsCompleted: false };
    public bool IsParticipantWorldApproved => _connection is { Completion.IsCompleted: false } connection && connection.Admission.WorldStartup.ParticipantStartApproved;
    private GoldSavedCharacterReferencePlan? _referencePlan;
    private GoldSavedCharacterImportPreparation? _importPreparation;
    private GoldParticipantCharacterUpload? _characterPackets;
    private GoldParticipantUploadSession? _uploadSession;
    private bool _uploadReported;
    private GameServerSectorInterest? _sectorInterest;
    private string _status = "offline";
    private string _admission = "transport diagnostics; native player profile and world entry pending";
    private int _eventsReceived;

    public string Status => $"{_status}; {_eventsReceived} events; {_objects.Count} base object states; {_movements.Count} movement refs; {_doors.Count} door refs; {_inventories.Count} inventory streams; {_admission}{StartupStatus}; {_allocatedReferences.Count} granted refs; {(_referencePlan is null ? "reference plan pending" : $"reference plan staged: hero {_referencePlan.PlayerSlot}, {_referencePlan.ObjectCount} objects")}{ImportStatus}; interest {_sectorInterest?.Status ?? "not requested"}";

    private string ImportStatus => _importPreparation is { } prepared
        ? $"; {prepared.ObjectPhysicalStates.Count} physical prefixes, player/shared inventory fix ready; {_characterPackets?.MessageCount ?? 0} character packets prepared; {UploadStatus}"
        : string.Empty;

    private string UploadStatus => _uploadSession is null ? "upload ready; derived values retained from save" :
        !_uploadSession.Completion.IsCompleted ? "upload sending" :
        _uploadSession.Completion.IsCompletedSuccessfully ? "upload sent; awaiting world/scene binding" : "upload failed; reconnect required";

    public string StageCharacterReferences(int selection, SacredSavedCharacterRecords source)
    {
        if (_uploadSession is not null) throw new InvalidOperationException("Disconnect before preparing another character upload.");
        if (_connection is not { Completion.IsCompleted: false } connection || connection.Admission.Phase != GoldAdmissionPhase.SlotAssigned)
            throw new InvalidOperationException("Reference preparation requires an active admitted connection.");
        if (connection.Admission.WorldStartup.AssignedClientHost)
            throw new InvalidOperationException("GameServer assigned the host role. Starting its first world is still pending; this join path requires a started host world.");
        if (_firstContact is null || _firstContact.CharacterType != selection || !_firstContact.Profile.SequenceEqual(source.Profile))
            throw new InvalidOperationException("Saved selection/profile differs from this connection's admission request.");
        var plan = GoldSavedCharacterReferencePlan.Create(source, connection.Admission.PlayerSlot, _allocatedReferences.ToArray());
        var prepared = GoldSavedCharacterImportPreparation.Create(plan);
        var packets = GoldSavedCharacterPacketPreparation.Create(prepared);
        ObjectReferences.Consume(plan.References.Values.Where(reference => reference != plan.PlayerSlot).ToArray());
        _referencePlan = plan;
        LocalMovementControl = source.Hero.Creature.MovementControl;
        _importPreparation = prepared;
        _characterPackets = packets;
        var message = $"reference plan staged: hero {plan.PlayerSlot}, {plan.ObjectCount} objects from {_allocatedReferences.Count} granted refs; upload pending";
        EngineLog.WriteLine($"Network: {message}.");
        EngineLog.WriteLine($"Network participant data order: {plan.ObjectCount} object streams 0x16E, hero 0x169, player/shared inventories 0x122; {packets.MessageCount} packets, {packets.PayloadBytes} bytes prepared.");
        EngineLog.WriteLine($"Network saved import: {prepared.ObjectPhysicalStates.Count} owned physical prefixes prepared; player/shared inventory fix ready, {prepared.Inventories.Primary.ChangedCachedTypes + prepared.Inventories.Shared.ChangedCachedTypes} cached types refreshed, {prepared.InventoryMembership.Count} inventory memberships bound; derived values retained from save.");
        return message;
    }

    public string StartCharacterUpload()
    {
        if (_uploadSession is not null) throw new InvalidOperationException("Character upload already started; disconnect before retrying.");
        if (_connection is null || _characterPackets is null) throw new InvalidOperationException("Connect and run set server-character plan <selection> before entering.");
        _uploadSession = GoldParticipantUploadSession.Start(_connection, _characterPackets);
        _uploadReported = false;
        var message = $"participant upload started: {_characterPackets.MessageCount} character packets between 0x15/0x17 controls";
        EngineLog.WriteLine($"Network: {message}.");
        return message;
    }

    public string RequestInitialSectors(SacredSectorIndex index)
    {
        if (_connection is not { Completion.IsCompleted: false } connection ||
            !connection.Admission.WorldStartup.ParticipantStartApproved || _uploadSession?.Completion.IsCompletedSuccessfully != true || _referencePlan is null)
            throw new InvalidOperationException("Initial sector interest requires a completed and approved participant upload.");
        if (_sectorInterest is not null) throw new InvalidOperationException("Initial sector interest already requested; disconnect before retrying.");
        var current = WorldReplicas.Objects[WorldReplicas.LocalPlayerSlot].BaseState.Sector;
        _sectorInterest = new(connection, WorldReplicas, index, () => checked((uint)ObjectReferences.Available));
        var message = _sectorInterest.Request(current);
        EngineLog.WriteLine($"Network: {message}.");
        return message;
    }

    public string RequestSectorInterest(ushort sector)
    {
        if (!IsParticipantWorldApproved || _sectorInterest is null) throw new InvalidOperationException("Connect, upload and request initial sectors before changing interest.");
        return _sectorInterest.Request(sector);
    }

    public Task SendPeerReadyAsync()
    {
        if (WorldReplicas.WorldTriggers.Pending.Count != 0)
            throw new InvalidOperationException($"Peer readiness requires native trigger world effects: {WorldReplicas.WorldTriggers.Pending.Count} pending. Inspect set server resources before retrying.");
        if (!IsParticipantWorldApproved || WorldReplicas.LocalPlayerSlot == 0 || WorldReplicas.Sectors.Count == 0 || !WorldReplicas.WorldBanks.IsComplete)
            throw new InvalidOperationException("Peer readiness requires approved character upload, confirmed world sectors and all four native world banks.");
        if (!WorldReplicas.SectorWork.IsComplete)
            throw new InvalidOperationException($"Peer readiness requires completion of requested native sector work: {WorldReplicas.SectorWork.PendingCount} pending, {WorldReplicas.SectorWork.FailedCount} failed. Inspect set server work before retrying.");
        return _connection!.SendAsync(GoldWorldEntryCodec.CreatePeerReady(PlayerSlot)).AsTask();
    }

    public Task SendOwnedMovementAsync(GoldMessage message)
    {
        if (!IsParticipantWorldApproved || message.Sender != PlayerSlot || message.Origin != PlayerSlot)
            throw new InvalidOperationException("Movement requires the current approved participant identity.");
        var decoded = GoldGameEventDecoder.Decode(message);
        var reference = decoded switch { GoldMovementEvent move => move.ObjectReference, GoldMovementEndEvent end => end.ObjectReference,
            GoldIdleEvent idle => idle.ObjectReference, _ => throw new ArgumentException("Expected a movement message.", nameof(message)) };
        if (reference != PlayerSlot) throw new InvalidOperationException("Local navigation can control only the owned hero.");
        if (!WorldReplicas.Objects.TryGetValue(reference, out var hero) || hero.BaseState.Owner != PlayerSlot ||
            (hero.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
            throw new InvalidOperationException("Movement requires an active hero still owned by this participant.");
        return _connection!.SendAsync(message).AsTask();
    }

    private string StartupStatus => _connection?.Admission is { Phase: GoldAdmissionPhase.SlotAssigned } admission
        ? $"; client host {admission.WorldStartup.AssignedClientHost}, host world available {admission.WorldStartup.HostWorldAvailable}, participant approved {admission.WorldStartup.ParticipantStartApproved}, sector entries {admission.WorldStartup.SectorEntryCount}"
        : string.Empty;

    public void SetFirstContact(GoldFirstContact? request)
    {
        if (_connection is { Completion.IsCompleted: false })
            throw new InvalidOperationException("Disconnect before changing the admission character.");
        _firstContact = request;
        _referencePlan = null;
        _importPreparation = null;
        _characterPackets = null;
        _admission = request is null ? "transport diagnostics; select a saved character for admission" :
            $"saved character selection {request.CharacterType} ready for FIRSTCONTACT; hero/world import pending";
        EngineLog.WriteLine($"Network admission: {_admission}.");
    }

    public bool TrySetOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = Status;
            foreach (var entry in _opcodeCounts) EngineLog.WriteLine($"Network opcode 0x{entry.Key:X4}: {entry.Value} events.");
        }
        else if (value.Equals("doors", StringComparison.OrdinalIgnoreCase))
            message = GameServerDoorDiagnostics.Write(WorldReplicas.DoorInteractions);
        else if (value.Equals("containers", StringComparison.OrdinalIgnoreCase))
        {
            var containers = WorldReplicas.ContainerInteractions;
            foreach (var pending in containers.Pending)
                EngineLog.WriteLine($"Network container activation pending: object {pending.Event.ObjectReference}, trigger {pending.Object.BaseState.TriggerReference}; native callback/model execution required.");
            message = $"{containers.ReceivedCount} container receives, {containers.AppliedCount} applied, {containers.AlreadyActivatedCount} already activated, " +
                $"{containers.MissingOrInactiveCount} missing/inactive/wrong type, {containers.Pending.Count} pending native activations";
        }
        else if (value.Equals("inventories", StringComparison.OrdinalIgnoreCase))
        {
            message = $"{_inventories.Count} retained Gold inventory streams; item/owner binding pending";
            foreach (var entry in _inventories.Values)
                EngineLog.WriteLine($"Network inventory {entry.InventoryId}: sender {entry.Message.Sender}, origin {entry.Message.Origin}, stream ID {entry.Inventory.InventoryId}, owner ref {entry.Inventory.OwnerReference}, grid {entry.Inventory.Width}x{entry.Inventory.Height}, {entry.Inventory.QuestCount} quest / {entry.Inventory.ScrollCount} scroll records, {entry.Inventory.RemainingData.Length} trailing bytes.");
        }
        else if (value.Equals("sectors", StringComparison.OrdinalIgnoreCase))
        {
            var startup = _connection?.Admission.WorldStartup;
            message = $"{startup?.SectorEntryCount ?? 0} native sector entry replies; {WorldReplicas.Sectors.Count} active confirmed sectors, center {WorldReplicas.SectorInterest.Center}; {WorldReplicas.SectorWork.PendingCount} pending native sector operations";
            if (startup?.LastSectorEntry is { } sectors)
            {
                EngineLog.WriteLine($"Network sector entry: retained words 0x{sectors.Word0:X8}/0x{sectors.Word58:X8}.");
                for (var index = 0; index < sectors.Count; index++)
                {
                    var slot = sectors.GetSlot(index);
                    EngineLog.WriteLine($"  Slot {index}: sector {slot.Sector}, present {slot.IsPresent}, script mode {slot.ScriptMode}, initialize flag {slot.InitializeFlag}, retained word 0x{slot.RetainedWord:X4}.");
                }
            }
        }
        else if (value.Equals("work", StringComparison.OrdinalIgnoreCase))
        {
            var queue = WorldReplicas.SectorWork;
            foreach (var work in queue.Work.Values)
                EngineLog.WriteLine($"Network sector work {work.Session}/{work.Id}: reply {work.Reply}, slot {work.Slot}, sector {work.Entry.Sector}, script mode {work.Entry.ScriptMode}, execute scripts {work.ExecuteScripts}, initialize {work.Entry.InitializeFlag}; {work.State}; {work.Error ?? "no error"}.");
            message = $"{queue.PendingCount} pending sector operations, {queue.FailedCount} failed; receiving a sector does not complete its native work";
        }
        else if (value.Equals("banks", StringComparison.OrdinalIgnoreCase))
            message = GameServerWorldBankDiagnostics.Write(WorldReplicas.WorldBanks);
        else if (value.Equals("regions", StringComparison.OrdinalIgnoreCase))
            message = GameServerWorldBankDiagnostics.WriteRegions(WorldReplicas.Regions);
        else if (value.Equals("resources", StringComparison.OrdinalIgnoreCase))
            message = GameServerWorldBankDiagnostics.WriteResources(WorldReplicas);
        else if (value.Equals("statistics", StringComparison.OrdinalIgnoreCase))
        {
            var states = WorldReplicas.Statistics.States;
            message = $"{states.Count} native statistics snapshots retained; counter meanings pending";
            foreach (var entry in states)
                EngineLog.WriteLine($"Network statistics sender {entry.Key.Sender}, origin {entry.Key.Origin}: {entry.Value.PrimaryData.Length} primary bytes, {entry.Value.Extension.Data.Length} extension bytes; suffix words [{string.Join(", ", Enumerable.Range(0, 25).Select(entry.Value.Extension.GetWord))}].");
        }
        else if (value.Equals("scripts", StringComparison.OrdinalIgnoreCase))
        {
            var registrations = WorldReplicas.DeathScripts.Registrations;
            var deletions = WorldReplicas.MovementTriggerDeletions.Instructions;
            message = $"{registrations.Count} native death-script registrations; {GameServerMovementTriggerDiagnostics.Write(WorldReplicas)}; script execution pending";
            foreach (var script in registrations)
                EngineLog.WriteLine($"Network death script for object {script.ObjectReference}: {script.ScriptText}.");
            foreach (var obj in WorldReplicas.Objects.Values.Where(obj => obj.BaseState.TriggerReference != 0))
                EngineLog.WriteLine($"Network object {obj.BaseState.ObjectReference}: trigger {obj.BaseState.TriggerReference}.");
            foreach (var deletion in deletions)
                EngineLog.WriteLine($"Network movement-trigger deletion '{deletion.Name}': {(deletion.AppliesToAllSectors ? "all sectors" : $"sector {deletion.Sector} and authored neighbors")}; trigger-bank binding pending.");
        }
        else if (value.Equals("objects", StringComparison.OrdinalIgnoreCase))
        {
            message = $"{_objects.Count} retained Gold object streams; creature prefix decoded, actor import pending";
            foreach (var entry in _objects)
            {
                var obj = entry.Value;
                var state = WorldReplicas.Objects.TryGetValue(entry.Key, out var replica) ? replica.BaseState : obj.BaseState;
                EngineLog.WriteLine($"Network object {entry.Key}: type {state.ObjectType}, owner {state.Owner}, sector {state.Sector}, position {state.X}/{state.Y}, surface {state.Surface}, trigger {state.TriggerReference}, flags 0x{state.Flags:X8}.");
                if (replica?.MountPartnerReference is { } partner)
                    EngineLog.WriteLine($"  Current mount partner ref {partner}.");
                if (obj.PhysicalState is { } physical)
                    EngineLog.WriteLine($"  Physical vector {physical.SelectedVector}, flags +0xB0 0x{physical.WordB0:X8}; world-coordinate interpretation pending.");
                if ((replica?.Creature ?? obj.CreatureState) is { } creature)
                    EngineLog.WriteLine($"  Creature equipment refs [{string.Join(", ", creature.EquipmentReferences.ToArray())}], {creature.RemainingData.Length} variable subtype bytes retained.");
                if (obj.CreatureTail is { } tail)
                    EngineLog.WriteLine($"  Creature values {(tail.Values.IsFull ? "full" : "compact")}: {tail.Values.Count22}/{tail.Values.Count38} value records, {tail.Count} nine-byte records, trailer mode {tail.Mode}, {tail.RemainingData.Length} unparsed bytes; actor import pending.");
                if (obj.ItemState is { } item)
                    EngineLog.WriteLine($"  Item resource {item.Instance.ItemType}, level {item.Instance.Level}, price {item.Instance.Price}, {item.Count} item records, weapon extension {item.HasWeaponExtension}, inventory membership {item.InventoryId}, {item.RemainingData.Length} unparsed bytes; actor import pending.");
            }
        }
        else if (value.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            Disconnect();
            message = "server disconnected";
        }
        else if (value.Equals("enter", StringComparison.OrdinalIgnoreCase))
        {
            try { message = StartCharacterUpload(); }
            catch (InvalidOperationException error) { message = error.Message; }
        }
        else if (Uri.TryCreate("tcp://" + value, UriKind.Absolute, out var endpoint) &&
                 endpoint.Port is > 0 and <= 65535 && endpoint.AbsolutePath == "/" &&
                 string.IsNullOrEmpty(endpoint.UserInfo) && string.IsNullOrEmpty(endpoint.Query) && string.IsNullOrEmpty(endpoint.Fragment))
        {
            Disconnect();
            _connection = new GoldGameConnection();
            if (_firstContact is { } firstContact) _connection.Start(endpoint.DnsSafeHost, endpoint.Port, firstContact);
            else _connection.Start(endpoint.DnsSafeHost, endpoint.Port);
            _status = $"connecting to {endpoint.Host}:{endpoint.Port}";
            message = _status;
        }
        else message = "use set server <host:port|enter|status|objects|doors|inventories|sectors|work|scripts|statistics|banks|regions|resources|disconnect>; select the gameplay TCP port";
        return true;
    }

    public void Update()
    {
        if (_connection is not { } connection) return;
        if (!_uploadReported && _uploadSession?.Completion is { IsCompleted: true } upload)
        {
            _uploadReported = true;
            if (upload.IsCompletedSuccessfully && _characterPackets is { } packets) _gameplay.SeedCharacter(packets);
            EngineLog.WriteLine(upload.IsCompletedSuccessfully ? "Network character upload complete: participant finish sent; waiting for server approval and sector data." :
                $"Network character upload failed: {upload.Exception?.GetBaseException().Message ?? "cancelled"}; reconnect required.");
        }
        for (var i = 0; i < 16 && connection.Events.TryRead(out var entry); i++)
        {
            switch (entry)
            {
                case GoldTransportConnected connected:
                    _status = $"TinCat connected, transport ID {connected.ConnectionId}";
                    EngineLog.WriteLine($"Network: {_status}; Sacred admission has not completed.");
                    break;
                case GoldFirstContactSent:
                    _admission = "FIRSTCONTACT sent; waiting for Sacred slot response";
                    EngineLog.WriteLine($"Network admission: {_admission}.");
                    break;
                case GoldMessagesReceived batch:
                    try { GoldGameDispatcher.Dispatch(batch.Messages, Receive); }
                    catch (InvalidDataException e)
                    {
                        EngineLog.WriteLine($"Network batch rejected: {e.Message}");
                        connection.Stop();
                    }
                    break;
                case GoldConnectionClosed closed:
                    _gameplay.Clear();
                    _status = $"closed: {closed.Reason}";
                    EngineLog.WriteLine($"Network: {_status}.");
                    break;
            }
        }
        if (connection.Completion.IsCompleted && connection.Events.Completion.IsCompleted &&
            !_status.StartsWith("closed:", StringComparison.Ordinal))
            _status = "closed: transport ended";
        _sectorInterest?.Update();
    }

    private void Receive(GoldGameEvent gameEvent)
    {
        _gameplay.Apply(gameEvent);
        var opcode = gameEvent.Message.Opcode;
        _opcodeCounts.TryGetValue(opcode, out var count);
        _opcodeCounts[opcode] = count + 1;
        _eventsReceived++;
        // Bounded diagnostics until native object admission/removal and reference mappings are implemented.
        switch (gameEvent)
        {
            case GoldObjectFlightEvent flight:
                WorldReplicas.Objects.TryGetValue(flight.ObjectReference, out var flyingObject);
                EngineLog.WriteLine($"Network item flight: ref {flight.ObjectReference}, opcode 0x{flight.Message.Opcode:X3}, sender {flight.Message.Sender}, origin {flight.Message.Origin}, owner {flyingObject?.BaseState.Owner}; applied {flyingObject?.Flight == flight}; arc {flight.IsItemArc}, height {flight.InitialHeight}.");
                break;
            case GoldAdmissionEvent admission:
                _admission = $"Sacred slot {admission.PlayerSlot}, flags 0x{admission.Flags:X2}; initial character/world import pending";
                EngineLog.WriteLine($"Network admission: {_admission}.");
                EngineLog.WriteLine($"Network startup{StartupStatus}; hero/inventory upload pending.");
                break;
            case GoldRejectionEvent rejection:
                _admission = $"rejected: reason {rejection.Reason}, data mismatch mask 0x{rejection.DataMismatchMask:X8}";
                EngineLog.WriteLine($"Network admission: {_admission}.");
                _connection?.Stop();
                break;
            case GoldPlayerDataEvent playerData:
                EngineLog.WriteLine($"Network player information: character type {playerData.PlayerData.CharacterType}, version {playerData.PlayerData.Version}; profile import pending.");
                break;
            case GoldHostWorldAvailableEvent:
                EngineLog.WriteLine($"Network host world available: origin {gameEvent.Message.Origin}; participant hero/inventory upload pending.");
                break;
            case GoldParticipantStartApprovedEvent:
                _admission = $"Sacred slot {_connection?.Admission.PlayerSlot}, flags 0x{_connection?.Admission.Flags:X2}; participant approved; scene binding pending";
                EngineLog.WriteLine("Network participant start approved by GameServer; scene binding pending.");
                break;
            case GoldSectorEntryEvent sectors:
                EngineLog.WriteLine($"Network sector entry: {sectors.Entry.Count} retained slots; use set server sectors; world/script application pending.");
                break;
            case GoldPlayerRosterEvent roster:
                EngineLog.WriteLine($"Network server roster: {roster.Roster.Count} retained records; server metadata at index 0, player slots 1..16; actor/world binding pending.");
                break;
            case GoldTimeProbeEvent:
                EngineLog.WriteLine($"Network time probe: {(_connection?.Admission.Phase == GoldAdmissionPhase.SlotAssigned ? "native reply sent" : "retained in transport diagnostics")}.");
                break;
            case GoldReferenceBatchEvent references:
                ObjectReferences.Apply(references.Batch);
                foreach (var reference in references.Batch.References)
                    if (_referenceSet.Add(reference)) _allocatedReferences.Add(reference);
                EngineLog.WriteLine($"Network allocated object references: {references.Batch.Count}; hero/player references are separate; world import pending.");
                break;
            case GoldReferenceRangeEvent range:
                EngineLog.WriteLine($"Network world-trigger reference range: {range.Start} + {range.Count}; {WorldReplicas.TriggerReferences.Available} IDs available; authored trigger creation pending.");
                break;
            case GoldPasswordResultEvent password:
                EngineLog.WriteLine($"Network game password: {(password.Accepted ? "accepted" : "rejected")}; character/world entry pending.");
                break;
            case GoldMovementEvent movement when _movements.ContainsKey(movement.ObjectReference) || _movements.Count < 4096:
                _movements[movement.ObjectReference] = [movement.Word1, movement.Word2, movement.Word3, movement.Word4, movement.Word5];
                break;
            case GoldMovementEndEvent end:
                _movements.Remove(end.ObjectReference);
                break;
            case GoldObjectRemovalEvent:
            case GoldObjectOwnerEvent:
                GameServerObjectLifecycleDiagnostics.Apply(_movements, _doors, gameEvent);
                break;
            case GoldObjectPositionEvent position:
                _movements.Remove(position.ObjectReference);
                break;
            case GoldIdleEvent idle:
                _movements.Remove(idle.ObjectReference);
                break;
            case GoldDoorEvent door when _doors.ContainsKey(door.ObjectReference) || _doors.Count < 4096:
                _doors[door.ObjectReference] = door.Open;
                EngineLog.WriteLine($"Network door receive: object {door.ObjectReference}, requested {(door.Open ? "open" : "closed")}; " +
                    $"{WorldReplicas.DoorInteractions.Pending.Count} pending native activations. Inspect set server doors.");
                break;
            case GoldContainerActivationEvent container:
                EngineLog.WriteLine($"Network container receive: object {container.ObjectReference}; applied {WorldReplicas.ContainerInteractions.AppliedCount}, " +
                    $"already activated {WorldReplicas.ContainerInteractions.AlreadyActivatedCount}, pending {WorldReplicas.ContainerInteractions.Pending.Count}.");
                break;
            case GoldInventoryEvent inventory when _inventories.ContainsKey((inventory.Message.Sender, inventory.Message.Origin, inventory.InventoryId)) || _inventories.Count < 64:
                _inventories[(inventory.Message.Sender, inventory.Message.Origin, inventory.InventoryId)] = inventory;
                break;
            case GoldObjectEvent obj when _objects.ContainsKey(obj.ObjectReference) || _objects.Count < 4096:
                _objects[obj.ObjectReference] = obj;
                break;
        }
        if (count == 0)
            EngineLog.WriteLine($"Network dispatch: 0x{opcode:X4}, {gameEvent.GetType().Name}, {gameEvent.Message.Payload.Length} bytes; retained for admission/reference mapping.");
    }

    private void Disconnect()
    {
        if (_connection is { } connection) connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _connection = null;
        _movements.Clear(); _objects.Clear(); _doors.Clear(); _opcodeCounts.Clear();
        _inventories.Clear();
        _allocatedReferences.Clear(); _referenceSet.Clear(); _referencePlan = null; _importPreparation = null;
        ObjectReferences.Clear();
        _characterPackets = null;
        if (_uploadSession is { } session)
        {
            try { session.Completion.GetAwaiter().GetResult(); }
            catch (Exception error) { EngineLog.WriteLine($"Network upload ended on disconnect: {error.Message}."); }
        }
        _uploadSession = null; _uploadReported = false;
        _sectorInterest?.ObserveCompletion();
        _sectorInterest = null;
        _gameplay.Clear();
        _eventsReceived = 0;
        LocalMovementControl = 0;
        _status = "offline";
        _admission = _firstContact is null ? "transport diagnostics; select a saved character for admission" :
            $"saved character selection {_firstContact.CharacterType} ready; hero/world import pending";
    }

    public void Dispose() => Disconnect();
}
