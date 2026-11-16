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
            var character = await session.PrepareCharacterAsync(cancel.Token);
            RendererLog.Info($"Host character: {character.Source.Profile.Name}, {character.ReferencePlan.ObjectCount} saved objects rebased with actual grants; packets retained privately.");
            RendererLog.Info("StartCode waits for native actor restoration and entry placement; connection retained. Console commands: status, character, banks, region <sector>, quit.");
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
                        var startup = session.Startup ?? throw new InvalidOperationException(character.ActorBindingBlocker);
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
                    case "quit": case "exit": return 3; // initialization is still incomplete
                    case "status":
                        RendererLog.Info($"Host status: {session.Phase}, live {session.IsCurrent}, slot {session.Admission.PlayerSlot}, IDs {session.References.Available}/{session.References.Issued}, trigger IDs {session.Replicas.TriggerReferences.Available}.");
                        RendererLog.Info(session.Startup is { } startupState
                            ? $"Startup: commands {startupState.Execution.ExecutedCommands}, complete {startupState.Execution.StartupCompleted}, invalidated {startupState.Execution.IsInvalidated}."
                            : $"StartCode not started: {character.ActorBindingBlocker}");
                        break;
                    case "character":
                        var retained = await session.PrepareCharacterAsync(cancel.Token);
                        var saved = retained.Source.Records.Hero.Creature.BaseState;
                        RendererLog.Info($"Host character: current {retained.IsCurrent}, slot {retained.ReferencePlan.PlayerSlot}, {retained.ReferencePlan.ObjectCount} rebased objects, {retained.Packets.MessageCount} private packets, saved pose {saved.Sector}:{saved.X},{saved.Y},{saved.Surface}; native entry pose pending.");
                        var entry = retained.EntryPosition;
                        RendererLog.Info($"Host authored entry: MultiStart.bin {entry.Sector}:{entry.X},{entry.Y},{entry.Surface}; native sector resolution and actor placement pending.");
                        var constructor = retained.HeroConstruction;
                        RendererLog.Info($"Host hero constructor: resource {constructor.Template.ItemType}, class {constructor.Template.Class}, factory reference {constructor.Actor.Base.ObjectReference}, active {constructor.Actor.Node.Active}, allocation counter {constructor.Counter.Value}; saved actor import/model restoration pending.");
                        RendererLog.Info($"Host hero animation: selection {retained.AnimationRequest.Selection}, requires activation {retained.AnimationRequest.RequiresActivation}; loop argument {retained.AnimationRequest.FirstArgument}, speed argument {retained.AnimationRequest.SecondArgument}; native 0x5559E0 activation and 0x5930B0 animation restoration pending.");
                        if (constructor.ModelResources is { } model)
                            RendererLog.Info($"Host hero CPU resources: {model.ModelName}, texture {model.Texture}, motion {model.SavedAnimation.Name}, bones {model.Body.Skin!.Skeleton.Bones.Length}, native local root {model.RootLocalOffset}; current {!model.IsInvalidated}. Activation/timing remain pending.");
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
                    default: RendererLog.Info("Host console commands: status, character, banks, region <sector>, quit."); break;
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
