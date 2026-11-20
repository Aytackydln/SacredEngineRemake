using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Networking;

namespace Sacred.World.Renderer.Terminal;

/// <summary>Headless composition of the production host initialization lifetime.
/// No renderer/scene readiness is granted by a completed startup script.</summary>
internal static class HostInitializationConsole
{
    public static async Task<int> RunAsync(RendererOptions options, int profile)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var campaign = SacredCampaignFiles.Resolve(options.GameDirectory, options.CampaignScriptsDirectory);
            await using var session = WorldHostInitializationSession.OpenSavedCharacter(options.GameDirectory, campaign,
                profile, options.NetworkProbeHost, options.NetworkProbePort, RendererLog.Info, cancel.Token);
            if (options.MinimalHost)
            {
                using var approvalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                approvalTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                await session.JoinMinimalAsync(approvalTimeout.Token);
            }
            else await session.PublishCharacterAsync(cancel.Token);
            var character = session.Character;
            var referencePlan = session.MinimalCharacterReferences ?? character!.ReferencePlan;
            RendererLog.Info($"Host character: {session.SelectedCharacter!.Profile.Name}, {referencePlan.ObjectCount} saved objects rebased with actual grants.");
            if (!options.MinimalHost) RendererLog.Info(session.ActiveHero!.StartupBlocker);
        RendererLog.Info("Host connection retained. Console commands: status, character, startup, campaign, interest, sector, enter-region, enter-sector, ambient, restore-sector, patch-sector, complete-sector, sectors, banks, region <sector>, quit.");
            while (session.IsCurrent)
            {
                var input = Console.In.ReadLineAsync(cancel.Token).AsTask();
                if (await Task.WhenAny(input, session.Completion) == session.Completion) break;
                var command = await input;
                if (command is null)
                {
                    // Redirected stdin closing is not a reason to discard the host.
                    await session.Completion.WaitAsync(cancel.Token); break;
                }
                RendererLog.Info($"Host console input: {command}");
                if (command.Trim().StartsWith("region ", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var startup = session.Startup ?? throw new InvalidOperationException(character?.ActorBindingBlocker ?? "Campaign execution is deferred in minimal host mode.");
                        if (!uint.TryParse(command.Trim()[7..], out var sector)) throw new InvalidOperationException("region requires an original sector ID.");
                        var result = await session.InitializeRegionAsync(options.GameDirectory, sector, cancel.Token);
                        var region = session.RegionInitialization!;
                        RendererLog.Info($"Host region: sector {sector}, native result {result?.ToString() ?? "pending"}, private flags sends {region.Publications.Count}, pending functions {string.Join(',', region.Lifecycle.PendingFunctions)}, {region.Blocker ?? "no callback pending"}.");
                        RendererLog.Info($"Host region effects: {region.Lifecycle.Records.Count} completed regions, {startup.Objects.Creatures?.Prepared.Count} prepared NPCs, {startup.Execution.Quests?.ActivationPublications.Count} private quest updates; remaining object IDs {session.References.Available}.");
                        RendererLog.Info("Visited region work does not establish final world globals, actor readiness or upload approval.");
                    }
                    catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                    { RendererLog.Info($"Host region pending: {error.Message}"); }
                    continue;
                }
                switch (command.Trim().ToLowerInvariant())
                {
                    case "quit": case "exit": return session.Phase == WorldHostInitializationPhase.MinimalUploadApproved ? 0 : 3;
                    case "status":
                        RendererLog.Info($"Host current roster player count: {session.Replicas.Session.Lobby.Control.Roster.NativePlayerCount?.ToString() ?? "unknown; complete server roster not received"}.");
                        RendererLog.Info($"Host status: {session.Phase}, live {session.IsCurrent}, slot {session.Admission.PlayerSlot}, IDs {session.References.Available}/{session.References.Issued}, trigger IDs {session.Replicas.TriggerReferences.Available}, current character sent {session.CharacterPublicationSent}, world replicas {session.Replicas.Objects.Count}, local replica slot {session.Replicas.LocalPlayerSlot}.");
                        if (options.MinimalHost)
                            RendererLog.Info($"Host initial interest confirmed: {session.Replicas.SectorInterest.IsConfirmed}, active sectors {session.Replicas.Sectors.Count}.");
                        RendererLog.Info(session.Startup is { } startupState
                            ? $"Startup: commands {startupState.Execution.ExecutedCommands}, complete {startupState.Execution.StartupCompleted}, invalidated {startupState.Execution.IsInvalidated}."
                            : options.MinimalHost ? "Minimal host mode. Campaign execution is deferred." :
                                $"StartCode not started: {session.ActiveHero?.StartupBlocker ?? character?.ActorBindingBlocker}");
                        break;
                    case "character":
                        if (options.MinimalHost)
                        {
                            RendererLog.Info($"Host character: {session.SelectedCharacter!.Profile.Name}, slot {session.Replicas.LocalPlayerSlot}, {referencePlan.ObjectCount} restored saved objects.");
                            break;
                        }
                        var retained = await session.PrepareCharacterAsync(cancel.Token);
                        var saved = retained.Source.Records.Hero.Creature.BaseState;
                        RendererLog.Info($"Host character: current {retained.IsCurrent}, slot {retained.ReferencePlan.PlayerSlot}, {retained.ReferencePlan.ObjectCount} rebased objects, {retained.Packets.MessageCount} private packets, saved pose {saved.Sector}:{saved.X},{saved.Y},{saved.Surface}; campaign entry placed {retained.HeroConstruction.Actor.CampaignEntryPlaced}.");
                        var entry = retained.EntryPosition;
                        RendererLog.Info($"Host authored entry: MultiStart.bin {entry.Sector}:{entry.X},{entry.Y},{entry.Surface}; actor placement completed {retained.HeroConstruction.Actor.CampaignEntryPlaced}.");
                        var constructor = retained.HeroConstruction;
                        if (constructor.Actor.RestoredCreature is { } values)
                            RendererLog.Info($"Host hero values: restored True, level {System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(constructor.Actor.Component.Values.AsSpan(78))}, arts {values.CombatArts.Count}, combos {values.Combos.Count}, status {values.StatusModifiers.Count}, saved equipment refs {values.PendingEquipmentReferences.Count(reference => reference != 0)}; native saved-world prelude and campaign entry integrated.");
                        RendererLog.Info($"Host hero factory: resource {constructor.Template.ItemType}, class {constructor.Template.Class}, reference {constructor.Actor.Base.ObjectReference}, active {constructor.Actor.Node.Active}, allocation counter {constructor.Counter.Value}, physical imported {constructor.Actor.RestoredPhysical is not null}, flags 0x{constructor.Actor.Base.Flags:X8}; local factory bound {session.ActiveHero is not null}.");
                            RendererLog.Info($"Host saved items: restored {constructor.SavedItemsRestored}, factory objects {constructor.EquipmentFactory.Items.Count}, active {constructor.EquipmentFactory.Items.Values.Count(item => item.Node.Active)}; current saved objects retained privately; no world publication/readiness.");
                        RendererLog.Info($"Host C0 post-load: completed {constructor.Actor.HeroPostLoadCompleted}, KI {constructor.Actor.KiMode}, EI {constructor.Actor.EiMode}, locomotion {constructor.Actor.LocomotionAction}; existing node/model retained, no world readiness.");
                        RendererLog.Info($"Host C2 binding: normalized {constructor.SavedItemPostLoadCompleted}, equipment refs {constructor.Actor.EquipmentReferences.Count(reference => reference != 0)}, inventory members {constructor.InventoryBinding?.Membership.Count}; actual factory objects; current publication sent {session.CharacterPublicationSent}.");
                        RendererLog.Info($"Host hero animation: selection {constructor.Actor.RestoredPhysical?.Animation}, loop {constructor.Animation?.LoopCount}, speed {constructor.Animation?.PlaybackSpeed}, pose evaluated {constructor.CurrentPose?.IsEvaluated}, pose seconds {constructor.CurrentPose?.SampleTimeSeconds}; shared backend current {!constructor.IsInvalidated && constructor.CurrentPose is not null}.");
                        RendererLog.Info($"Host model reactivation: completed {constructor.Actor.ModelReactivated}, animation selector {constructor.Actor.RestoredPhysical?.Animation}, bind pose evaluated {constructor.BindPose?.Pose.IsEvaluated}; unloaded item slot cleanup only, no loaded attachment/readiness.");
                        RendererLog.Info($"Host saved-loader tail: prepared {constructor.Actor.SavedLoaderTailPrepared}, heading {constructor.Actor.Direction}; final slots and both inventories fixed; campaign entry follows this stage.");
                        RendererLog.Info($"Host world entry: prelude {constructor.Actor.SavedWorldPreludeCompleted}, placed {constructor.Actor.CampaignEntryPlaced}, position {constructor.Actor.Base.Sector}:{constructor.Actor.Base.X},{constructor.Actor.Base.Y},{constructor.Actor.Base.Surface}, loaded sectors {constructor.Terrain?.LoadedSectorCount}, primary quests {constructor.InventoryBinding?.Primary.Inventory.QuestCount}; startup complete {session.Startup?.Execution.StartupCompleted == true}, campaign complete {session.Startup?.Execution.PostStartup.IsCompleted == true}.");
                        RendererLog.Info($"Host current publication: prepared {constructor.RuntimePublication is not null}, sent {session.CharacterPublicationSent}, messages {constructor.RuntimePublication?.MessageCount}, native value snapshots {constructor.ValueSnapshots is not null}; world approval/readiness pending.");
                        if (constructor.ModelResources is { } model)
                            RendererLog.Info($"Host hero CPU resources: {model.ModelName}, texture {model.Texture}, motion {model.SavedAnimation.Name}, bones {model.Body.Skin!.Skeleton.Bones.Length}, native local root {model.RootLocalOffset}; current {!model.IsInvalidated}.");
                        break;
                    case "campaign":
                        try { RendererLog.Info($"Host campaign callback complete: {await session.ContinueCampaignAsync(cancel.Token)}."); }
                        catch (InvalidOperationException error) { RendererLog.Info($"Host campaign pending: {error.Message}"); }
                        break;
                    case "enter-region":
                        try
                        {
                            var init=session.RegionInitialization?.InitialSector ?? throw new InvalidOperationException("Run the requested sector Init first.");
                            var result=await init.ContinueRegionEntryAsync(cancel.Token);
                            RendererLog.Info($"Host region entry: result {result?.ToString() ?? "pending"}, pending functions {string.Join(',',session.RegionInitialization!.Entry?.PendingFunctions ?? [])}; {init.EnterRegionBlocker ?? "sector Enter/patch/notification still required"}.");
                        }
                        catch(Exception error) when(error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host region entry pending: {error.Message}"); }
                        break;
                    case "enter-sector":
                        try
                        {
                            var init = session.RegionInitialization?.InitialSector ?? throw new InvalidOperationException("Run the requested Init and region entry first.");
                            var result = await init.ContinueSectorEntryAsync(cancel.Token);
                            var entryWork = init.SectorEntry!;
                            RendererLog.Info($"Host sector Enter: callback complete {result}, function {entryWork.FunctionIndex}, connection setting {session.NetworkSpeedSettings}, skipped NPCs {session.Startup!.Objects.Creatures!.SkippedEntryCreatures}, filtered population commands {entryWork.PopulationCommands}, spawn rules {session.Startup.Execution.SpawnEquipment.Count}; {entryWork.Blocker ?? "post-callback area population/restore/patch/notification still required"}.");
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host sector Enter pending: {error.Message}"); }
                        break;
                    case "sectors":
                        try
                        {
                            var complete = await WorldHostSectorSequence.RunAsync(session, options.GameDirectory, RendererLog.Info, cancel.Token);
                            RendererLog.Info($"Host ordered sectors: complete {complete}, pending {session.Replicas.SectorWork.PendingCount}; full banks/upload/readiness remain required.");
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host ordered sectors pending: {error.Message}"); }
                        break;
                    case "complete-sector":
                        try
                        {
                            var init = session.RegionInitialization?.InitialSector ?? throw new InvalidOperationException("Run the requested sector work first.");
                            var complete = await init.ContinueCompletionAsync(cancel.Token);
                RendererLog.Info($"Host sector completion: sector {init.Completion!.Sector}, complete {complete}, weather {init.Completion.NotificationKind}, checked members {init.Completion.CheckedMembers}, activated {init.Completion.ActivatedActors}; queue state {session.Replicas.SectorWork.Work[init.Instruction!.Id].State}, pending {session.Replicas.SectorWork.PendingCount}; remaining initialization/upload/readiness still required.");
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host sector completion pending: {error.Message}"); }
                        break;
                    case "patch-sector":
                        try
                        {
                            var init = session.RegionInitialization?.InitialSector ?? throw new InvalidOperationException("Run the requested sector work first.");
                            var complete = await init.ContinueFinalPatchesAsync(cancel.Token);
                            RendererLog.Info($"Host final sector patches: complete {complete}, mouseovers {init.FinalPatches!.MouseoverCount}, trigger positions {init.FinalPatches.TriggerPositionCount}; local enter notification still required.");
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host final sector patches pending: {error.Message}"); }
                        break;
                    case "restore-sector":
                        try
                        {
                            var init = session.RegionInitialization?.InitialSector ?? throw new InvalidOperationException("Run the requested sector work first.");
                            var complete = await init.ContinueRestorationAsync(cancel.Token);
                            var remaining = init.FinalPatches?.IsCompleted == true ? "local enter notification" : "final patches/notification";
                            RendererLog.Info($"Host sector restoration: complete {complete}, fresh-source rows {init.Restoration!.SourceCount}, restored {init.Restoration.RestoredCount}; {remaining} still required.");
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host sector restoration pending: {error.Message}"); }
                        break;
                    case "ambient":
                        try
                        {
                            var init = session.RegionInitialization?.InitialSector ?? throw new InvalidOperationException("Run the requested Enter callback first.");
                            var result = await init.ContinueAmbientAsync(cancel.Token);
                            var ambient = init.AmbientPopulation!;
                            var members = session.Startup!.Objects.Creatures!.Terrain!.Membership.Get(init.Environment.Sector);
                            var remaining = init.FinalPatches?.IsCompleted == true ? "local enter notification" :
                                init.Restoration?.IsCompleted == true ? "final patches/notification" : "saved restoration/patch/notification";
                            RendererLog.Info($"Host ambient: complete {result}, planned {ambient.Planned}, actual actors {ambient.Created}, sent {ambient.Publications.Count}, sector members {ambient.References.Count(members.Contains)}; RNG 0x{session.ActiveHero!.RandomState:X8}, IDs {session.References.Available}/{session.References.Issued}; {remaining} still required.");
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host ambient pending: {error.Message}"); }
                        break;
                    case "interest":
                        try
                        {
                            await session.RequestInitialInterestAsync(options.GameDirectory,cancel.Token);
                            RendererLog.Info($"Host interest: center {session.Replicas.SectorInterest.Center}, confirmed {session.Replicas.SectorInterest.IsConfirmed}, sectors {session.Replicas.SectorInterest.Confirmed.Count}, pending work {session.Replicas.SectorWork.PendingCount}.");
                            foreach(var work in session.Replicas.SectorWork.Work.Values.OrderBy(work=>work.Id))
                                RendererLog.Info($"Host requested sector work: id {work.Id}, slot {work.Slot}, sector {work.Entry.Sector}, script mode {work.Entry.ScriptMode}, initialize {work.Entry.RequestsInitialization}, execute scripts {work.ExecuteScripts}, state {work.State}.");
                        }
                        catch(Exception error) when(error is InvalidOperationException or InvalidDataException or NotSupportedException or OperationCanceledException)
                        { RendererLog.Info($"Host interest pending: {error.Message}"); }
                        break;
                    case "sector":
                        try
                        {
                            var sector = session.Replicas.SectorWork.Work.Values.OrderBy(work=>work.Id)
                                .FirstOrDefault(work=>work.State is Sacred.Network.Gameplay.GoldSectorWorkState.Queued or Sacred.Network.Gameplay.GoldSectorWorkState.Executing)?.Entry.Sector
                                ?? session.ActiveHero?.Actor.Base.Sector ?? throw new InvalidOperationException("No entered hero.");
                            var result = await session.InitializeSectorAsync(options.GameDirectory,sector,cancel.Token);
                            var work = session.RegionInitialization!.InitialSector;
                            RendererLog.Info($"Host initial sector: {sector}, native result {result?.ToString() ?? "pending"}, complete {work?.IsCompleted}, monster bounds {work?.Environment.Value.MonsterMaxCount}/{work?.Environment.Value.MonsterMinLevel}/{work?.Environment.Value.MonsterMaxLevel}, trigger publications {session.Startup!.Triggers.Publications.Count}; work {work?.Instruction?.State}, pending sector work {session.Replicas.SectorWork.PendingCount}; {work?.Blocker ?? "post-Init sector work/upload/readiness remain pending"}.");
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
                        { RendererLog.Info($"Host initial sector pending: {error.Message}"); }
                        break;
                    case "banks":
                        try
                        {
                            foreach (var source in await session.PrepareBankSourcesAsync(cancel.Token))
                                RendererLog.Info($"Host bank source: {source.Name}, 0x{source.Bank:X3}/{source.Section?.ToString() ?? "global"}, count {source.Count}, bytes {source.Bytes.Length}, {source.Blocker ?? "private source prepared"}.");
                            RendererLog.Info("Private sources only; required callbacks, other sections, region flags and actor binding still gate a complete upload.");
                        }
                        catch (InvalidOperationException error) { RendererLog.Info($"Host bank preparation pending: {error.Message}"); }
                        break;
                    case "startup":
                        try { await session.InitializeAsync(options.GameDirectory, campaign, cancel.Token); }
                        catch (InvalidOperationException error) { RendererLog.Info($"Host startup pending: {error.Message}"); }
                        break;
            default: RendererLog.Info("Host console commands: status, character, startup, campaign, interest, sector, enter-region, enter-sector, ambient, restore-sector, patch-sector, complete-sector, sectors, banks, region <sector>, quit."); break;
                }
            }
            RendererLog.Info($"Host initialization disconnected: {session.Failure ?? "connection ended"}.");
            return 1;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return 3; }
        catch (Exception error) { Console.Error.WriteLine($"Host initialization failed: {error.Message}"); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }
}
