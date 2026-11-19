using System.Net;
using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pax.Hero;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;
using Sacred.GameplayHost.World;

namespace Sacred.GameplayHost.Networking;

/// <summary>Persistent first-host connection and ordered receive owner. The
/// connection performs keepalive while world preparation holds the state gate.
/// Disposal or connection loss invalidates all retained generated state.</summary>
public sealed class WorldHostInitializationSession : IAsyncDisposable
{
    private readonly GoldGameConnection _connection;
    private readonly Action<string> _log;
    private readonly uint _triggerBound;
    private readonly Func<GoldSessionState, IPEndPoint, CancellationToken, Task> _seed;
    private readonly CancellationTokenSource _stop;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TaskCompletionSource _reservations = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _requested;
    private bool _admissionObserved;
    private bool _startupAttempted;
    private bool _characterPublicationStarted;
    private bool _characterPublicationSent;
    private Task? _dispose;
    private readonly object _disposeGate = new();
    public SacredGameplayReplicaSession WorldSession { get; } = new();
    public GoldWorldReplica Replicas => WorldSession.Replicas;
    public WorldAdmissionReceipt? AdmissionReceipt => WorldSession.Admission.Current;
    public GoldObjectReferencePool References { get; } = new();
    public GoldAdmissionStatus Admission => _connection.Admission;
    public byte NetworkSpeedSettings { get; private init; }
    public WorldHostInitializationPhase Phase { get; private set; } = WorldHostInitializationPhase.Connecting;
    public WorldHostStartupState? Startup { get; private set; }
    public WorldHostSavedCharacterSource? SelectedCharacter { get; private set; }
    public WorldHostCharacterPreparation? Character { get; private set; }
    public WorldHostActiveHeroBinding? ActiveHero { get; private set; }
    public WorldHostStartupRequest? StartupRequest { get; private set; }
    public bool CharacterPublicationSent => _characterPublicationSent && IsCurrent && Character?.HeroConstruction.RuntimePublication is not null;
    public WorldHostRegionInitialization? RegionInitialization { get; private set; }
    public string? Failure { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool IsCurrent => !_stop.IsCancellationRequested && !_connection.IsStopping && !_connection.Completion.IsCompleted &&
        (!_requested || AdmissionReceipt is { IsCurrent: true });

    private WorldHostInitializationSession(GoldGameConnection connection, uint triggerBound,
        Func<GoldSessionState, IPEndPoint, CancellationToken, Task> seed, Action<string> log, CancellationToken token)
    {
        (_connection, _triggerBound, _seed, _log) = (connection, triggerBound, seed, log);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
    }
    public static WorldHostInitializationSession OpenSavedCharacter(string installation, SacredCampaignFiles campaign,
        int selection, string host, int port, Action<string> log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        cancellationToken.ThrowIfCancellationRequested();
        var source = WorldHostSavedCharacterSource.ReadSelected(installation, selection);
        var hero = source.Profile;
        var contact = GoldFirstContact.Create(hero.Profile, new(hero.Name, checked((byte)selection), 1, 1, 1, 0), GoldLanCompatibility.Read(installation));
        var bound = SacredWorldTriggerArchive.Load(Path.Combine(installation, "World", "Triggers.PAK")).Header.TriggerCount;
        return StartSavedCharacter(source, new(), host, port, contact, bound,
            (state, remote, token) => WorldHostStartupDiscovery.SeedAsync(installation, remote, contact.Version, state, log, token), log, cancellationToken);
    }
    public static WorldHostInitializationSession StartSavedCharacter(WorldHostSavedCharacterSource source,
        GoldGameConnection connection, string host, int port, GoldFirstContact contact, uint authoredTriggerBound,
        Func<GoldSessionState, IPEndPoint, CancellationToken, Task> seedSession, Action<string> log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Records.Profile.SequenceEqual(contact.Profile))
            throw new InvalidDataException("Host character source must match the actual FIRSTCONTACT profile.");
        var session = Start(connection, host, port, contact, authoredTriggerBound, seedSession, log, cancellationToken);
        session.SelectedCharacter = source;
        session.StartupRequest = WorldHostStartupRequest.FreshWorld;
        return session;
    }

    /// <summary>Prepare original source data before native actor restoration and
    /// script execution. Idempotent on this receipt; never sends character data.</summary>
    public async Task<WorldHostCharacterPreparation> PrepareCharacterAsync(CancellationToken cancellationToken = default)
    {
        await WaitForReservationsAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            if (Character?.HeroConstruction.RuntimePublication is not null) return Character;
            var source = SelectedCharacter ?? throw new InvalidOperationException("No selected saved-character source on this diagnostic connection.");
            if (_startupAttempted) throw new InvalidOperationException("Character import must precede StartCode and its shared reference allocations.");
            SetPhase(WorldHostInitializationPhase.PreparingCharacter);
            try
            {
                var flags = Replicas.Session.GameFlags ?? throw new InvalidOperationException("Character entry requires actual session flags.");
                var levelMode = Replicas.Session.NativeControlWord ?? throw new InvalidOperationException("Character entry requires the received native level-mode word.");
                Character ??= new(source, Admission.PlayerSlot, References, flags, levelMode, () => IsCurrent && Character is not null);
                await Character.HeroConstruction.PrepareModelAsync(source.Installation, Character.AnimationRequest,
                    () => IsCurrent && Character is not null, linked.Token).ConfigureAwait(false);
                RequireCurrent();
                Character.HeroConstruction.RestorePhysical(source.Installation, source.Records.Hero.Creature);
                Character.HeroConstruction.RestoreCreature(source.Installation, source.Records.Hero.Creature);
                Character.HeroConstruction.CompleteHeroPostLoad();
                Character.HeroConstruction.RestoreSavedItems(source.Installation, Character.ReferencePlan);
                Character.HeroConstruction.CompleteSavedItemPostLoad();
                Character.HeroConstruction.BindSavedEquipmentAndInventories(Character.ReferencePlan);
                Character.HeroConstruction.CompleteSavedWorldPrelude(source.Installation);
                Character.HeroConstruction.ReactivateBodyModel();
                Character.HeroConstruction.PrepareSavedLoaderTail();
                Character.HeroConstruction.PlaceCampaignEntry(Character.EntryPosition);
                Character.HeroConstruction.PrepareRuntimePublication(Character.ReferencePlan);
            }
            catch
            {
                // A failed/cancelled model read can retry on the same factory
                // and reference plan. Do not allocate saved objects again.
                SetPhase(Character is null ? WorldHostInitializationPhase.ReservationsAvailable : WorldHostInitializationPhase.WaitingForActorBinding);
                throw;
            }
            RequireCurrent();
            SetPhase(WorldHostInitializationPhase.WaitingForActorBinding);
            _log($"Host character prepared: selection {source.Selection}, slot {Character.ReferencePlan.PlayerSlot}, {Character.ReferencePlan.ObjectCount} objects, {Character.Packets.MessageCount} private packets, remaining actual IDs {References.Available}.");
            if (Character.HeroConstruction.ModelResources is { } model)
                _log($"Host hero physical import: {model.ModelName}, saved motion {model.SavedAnimation.Name}, root {model.RootLocalOffset}, animation loop {Character.HeroConstruction.Animation?.LoopCount}, speed {Character.HeroConstruction.Animation?.PlaybackSpeed}; same factory node retained through campaign placement.");
            if (Character.HeroConstruction.Actor.RestoredCreature is { } restored)
                _log($"Host hero value import: normalized scalar state, {restored.CombatArts.Count} acquired arts, {restored.Combos.Count} combos, {restored.StatusModifiers.Count} status modifiers; original saved references retained alongside the separately validated current graph.");
            _log($"Host saved item readers: {Character.HeroConstruction.EquipmentFactory.Items.Count} actual factory objects, shared allocation counter {Character.HeroConstruction.Counter.Value}; current saved objects retained privately; no world publication/readiness.");
            _log($"Host C0 post-load: completed {Character.HeroConstruction.Actor.HeroPostLoadCompleted}, KI {Character.HeroConstruction.Actor.KiMode}, EI {Character.HeroConstruction.Actor.EiMode}, locomotion {Character.HeroConstruction.Actor.LocomotionAction}; existing node/model retained, no world readiness.");
            _log($"Host C2 binding: normalized {Character.HeroConstruction.SavedItemPostLoadCompleted}, equipment refs {Character.HeroConstruction.Actor.EquipmentReferences.Count(reference => reference != 0)}, inventory members {Character.HeroConstruction.InventoryBinding?.Membership.Count}; actual factory objects; current-state publication and live actor binding pending.");
            _log($"Host model reactivation: completed {Character.HeroConstruction.Actor.ModelReactivated}, animation selector {Character.HeroConstruction.Actor.RestoredPhysical?.Animation}, bind pose evaluated {Character.HeroConstruction.BindPose?.Pose.IsEvaluated}; unloaded item slot cleanup only, no loaded attachment/readiness.");
            _log($"Host saved-loader tail: prepared {Character.HeroConstruction.Actor.SavedLoaderTailPrepared}, heading {Character.HeroConstruction.Actor.Direction}; final slots and both inventories fixed; campaign entry follows this stage.");
            _log($"Host world entry: prelude {Character.HeroConstruction.Actor.SavedWorldPreludeCompleted}, placed {Character.HeroConstruction.Actor.CampaignEntryPlaced}, position {Character.HeroConstruction.Actor.Base.Sector}:{Character.HeroConstruction.Actor.Base.X},{Character.HeroConstruction.Actor.Base.Y},{Character.HeroConstruction.Actor.Base.Surface}, loaded sectors {Character.HeroConstruction.Terrain?.LoadedSectorCount}, primary quests {Character.HeroConstruction.InventoryBinding?.Primary.Inventory.QuestCount}; current-state publication and live actor binding pending.");
            _log($"Host current publication: prepared {Character.HeroConstruction.RuntimePublication is not null}, messages {Character.HeroConstruction.RuntimePublication?.MessageCount}, native value snapshots {Character.HeroConstruction.ValueSnapshots is not null}; current actor/item/inventory bytes, not sent yet.");
            _log(Character.ActorBindingBlocker);
            return Character;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Takes ownership of a fresh connection and its single event reader.
    /// Session flag discovery is supplied; it must establish actual session facts.</summary>
    public static WorldHostInitializationSession Start(GoldGameConnection connection, string host, int port,
        GoldFirstContact contact, uint authoredTriggerBound,
        Func<GoldSessionState, IPEndPoint, CancellationToken, Task> seedSession, Action<string> log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(seedSession); ArgumentNullException.ThrowIfNull(log);
        cancellationToken.ThrowIfCancellationRequested();
        var owner = new WorldHostInitializationSession(connection, authoredTriggerBound, seedSession, log, cancellationToken)
            { NetworkSpeedSettings = contact.ConnectionSpeed };
        try
        {
            connection.Start(host, port, contact);
            owner.Completion = owner.ReceiveAsync();
            return owner;
        }
        catch { owner._stop.Dispose(); throw; }
    }

    public async Task WaitForReservationsAsync(CancellationToken cancellationToken = default)
    {
        await _reservations.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        RequireCurrent();
    }

    public async Task<GoldParticipantCharacterUpload> PublishCharacterAsync(CancellationToken cancellationToken = default)
    {
        var character = await PrepareCharacterAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            var runtime = character.HeroConstruction.RuntimePublication ?? throw new InvalidOperationException("Current native character export is absent.");
            if (_characterPublicationSent) { ActiveHero!.RequireCurrent(); return runtime; }
            if (_characterPublicationStarted) throw new InvalidOperationException("A partially sent character cannot be replayed on the same connection.");
            character.HeroConstruction.ValueSnapshots!.RequireMatch(character.HeroConstruction.Actor);
            var messages = runtime.CreateMessages();
            _characterPublicationStarted = true;
            foreach (var message in messages)
            {
                RequireCurrent();
                await _connection.SendAsync(message, linked.Token).ConfigureAwait(false);
            }
            RequireCurrent();
            ActiveHero = new(character);
            _characterPublicationSent = true;
            SetPhase(WorldHostInitializationPhase.WaitingForStartupInputs);
            _log($"Host current publication: sent {messages.Count} current messages once; {runtime.ObjectCount} owned items, hero 0x169, primary/shared 0x122. World approval and actor readiness remain pending.");
            _log($"Host active hero: bound actual factory ref {ActiveHero.Actor.Base.ObjectReference}, level {ActiveHero.Level}, evaluated pose {ActiveHero.Pose.IsEvaluated}, loaded sectors {ActiveHero.Terrain.LoadedSectorCount}; shared counter {ActiveHero.Counter.Value}.");
            return runtime;
        }
        catch
        {
            if (_characterPublicationStarted && !_characterPublicationSent) { _stop.Cancel(); _connection.Stop(); }
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<WorldHostStartupState> InitializeAsync(string installation, SacredCampaignFiles campaign,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        if (SelectedCharacter is not null)
        {
            await PublishCharacterAsync(cancellationToken).ConfigureAwait(false);
            ActiveHero!.RequireCurrent();
            StartupRequest!.RequireFreshWorld();
        }
        await WaitForReservationsAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        var started = false;
        try
        {
            RequireCurrent();
            if (_startupAttempted) throw new InvalidOperationException("Host startup cannot be replayed into the same reservation lifetime.");
            _startupAttempted = true; started = true; SetPhase(WorldHostInitializationPhase.ExecutingStartup);
            Startup = WorldHostStartupPreparation.Run(installation, campaign, Admission.PlayerSlot, Replicas, References, _log, linked.Token, ActiveHero, StartupRequest);
            RequireCurrent();
            SetPhase(Startup.Execution.StartupCompleted ? WorldHostInitializationPhase.StartupComplete : WorldHostInitializationPhase.StartupBlocked);
            _log("Host initialization retained on its live connection; full banks, callbacks, upload and actor readiness remain pending.");
            return Startup;
        }
        catch
        {
            if (started)
            {
                if (Startup is not null) Startup.Execution.IsInvalidated = true;
                SetPhase(WorldHostInitializationPhase.StartupBlocked);
            }
            throw;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Private source preparation only. Upload still requires complete
    /// callbacks, all sections/globals, actor state and reference validation.</summary>
    public async Task<bool> ContinueCampaignAsync(CancellationToken cancellationToken = default)
    {
        RequireCurrent();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            var complete = WorldHostCampaignContinuation.Run(this, _log, linked.Token);
            RequireCurrent();
            SetPhase(complete ? WorldHostInitializationPhase.CampaignComplete : WorldHostInitializationPhase.CampaignBlocked);
            return complete;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<WorldHostBankSource>> PrepareBankSourcesAsync(CancellationToken cancellationToken = default)
    {
        RequireCurrent();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            var startup = Startup ?? throw new InvalidOperationException("No generated host startup state.");
            var sources = WorldHostBankSourcePreparation.Prepare(startup);
            RequireCurrent();
            return sources;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool?> InitializeRegionAsync(string installation, uint sector, CancellationToken cancellationToken = default)
    {
        RequireCurrent();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            var startup = Startup ?? throw new InvalidOperationException("No generated startup state.");
            if (!startup.Execution.StartupCompleted || startup.Execution.IsInvalidated)
                throw new InvalidOperationException("Region work requires completed valid startup.");
            RegionInitialization ??= new(installation, startup, Admission.PlayerSlot);
            var result = RegionInitialization.TryInitialize(sector, linked.Token);
            RequireCurrent();
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task<bool?> InitializeSectorAsync(string installation, uint sector, CancellationToken cancellationToken = default) =>
        WorldHostSectorInitialization.RunOwnedAsync(this, _connection,installation,sector,_gate,_stop.Token,
            () => RegionInitialization ??= new(installation,Startup ?? throw new InvalidOperationException("No generated startup state."),Admission.PlayerSlot),cancellationToken);

    public Task RequestInitialInterestAsync(string installation, CancellationToken token = default) => WorldHostInitialInterest.RequestAsync(this,installation,_connection,_gate,_stop.Token,token);
    private async Task ReceiveAsync()
    {
        try
        {
            SetPhase(WorldHostInitializationPhase.WaitingForHostAssignment);
            await foreach (var entry in _connection.Events.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                await _gate.WaitAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    if (entry is GoldConnectionClosed closed) throw new IOException(closed.Reason);
                    if (entry is GoldMessagesReceived received)
                        GoldGameDispatcher.Dispatch(received.Messages, message =>
                        {
                            WorldSession.Apply(message);
                            if (message is GoldAdmissionEvent) _admissionObserved = true;
                            if (message is GoldReferenceBatchEvent batch)
                            {
                                References.Apply(batch.Batch);
                                _log($"Host initialization: received {batch.Batch.Count} object IDs; issued {References.Issued}, available {References.Available}.");
                            }
                        });
                    var admission = Admission;
                    if (admission.Phase == GoldAdmissionPhase.Rejected)
                        throw new InvalidDataException($"Host admission rejected: reason {admission.RejectionReason}, mismatch 0x{admission.DataMismatchMask:X8}.");
                    // Transport state can already reflect a later queued batch.
                    // Do not require its receipt while consuming earlier notices
                    // or game messages; first consume the actual admission event.
                    if (!_admissionObserved) continue;
                    if (admission.Phase != GoldAdmissionPhase.SlotAssigned) continue;
                    var world = admission.WorldStartup;
                    if (AdmissionReceipt is not { IsCurrent: true } receipt || receipt.PlayerSlot != admission.PlayerSlot)
                        throw new InvalidOperationException($"Host initialization lost its current admission receipt after {Describe(entry)}.");
                    if (!world.AssignedClientHost || world.ParticipantStartApproved ||
                        (world.HostWorldAvailable && !_connection.HostUploadApproved))
                        throw new InvalidOperationException($"Host initialization requires an empty server first-host assignment (slot {admission.PlayerSlot}, flags 0x{admission.Flags:X2}).");
                    if (!_requested)
                    {
                        await _seed(Replicas.Session, _connection.RemoteEndpoint ?? throw new IOException("No admitted TCP endpoint."), _stop.Token).ConfigureAwait(false);
                        if (Replicas.Session.GameFlags is null) throw new InvalidDataException("Host initialization requires advertised or received session flags.");
                        SetPhase(WorldHostInitializationPhase.WaitingForReservations);
                        // Native first-host captures request 2,000 trigger IDs.
                        // Both native first-host captures receive unsolicited
                        // object reservations. They can arrive in a later batch;
                        // do not race that delivery with the old diagnostic's
                        // extra 64-ID request. Exhaustion remains a real blocker.
                        await _connection.SendAsync(GoldReferenceCodec.CreateTriggerRangeRequest(_triggerBound, 2000, admission.PlayerSlot), _stop.Token).ConfigureAwait(false);
                        _requested = true;
                    }
                    if (Replicas.TriggerReferences.Available != 0 && References.Available != 0 && !_reservations.Task.IsCompleted)
                    {
                        SetPhase(WorldHostInitializationPhase.ReservationsAvailable);
                        _reservations.TrySetResult();
                    }
                }
                finally { _gate.Release(); }
            }
            if (!_stop.IsCancellationRequested) throw new IOException("Host connection event stream ended.");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { Failure = error.Message; _log($"Host initialization failed: {error.Message}"); }
        finally
        {
            _connection.Stop();
            await _connection.Completion.ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Startup is not null) Startup.Execution.IsInvalidated = true;
                RegionInitialization = null;
                Character?.HeroConstruction.Invalidate();
                ActiveHero = null;
                Character = null;
                WorldSession.Clear(); References.Clear();
                _reservations.TrySetException(new IOException(Failure ?? "Host initialization ended before reservations were available."));
                SetPhase(WorldHostInitializationPhase.Disconnected);
            }
            finally { _gate.Release(); }
        }
    }
    private void RequireCurrent()
    {
        if (!IsCurrent) throw new InvalidOperationException("Host initialization lifetime ended; retained state cannot be used.");
    }
    private static string Describe(GoldConnectionEvent entry) => entry is GoldMessagesReceived received
        ? string.Join(',', received.Messages.Select(message => $"0x{message.Opcode:X3}")) : entry.GetType().Name;
    private void SetPhase(WorldHostInitializationPhase phase)
    {
        Phase = phase; _log($"Host initialization phase: {phase}.");
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_dispose ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        _stop.Cancel(); _connection.Stop();
        await Completion.ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}
