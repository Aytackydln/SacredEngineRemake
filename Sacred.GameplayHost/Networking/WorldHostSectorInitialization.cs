using Sacred.Core.GameBin.Scripts;
using Sacred.Assets.GameBin;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>One server-requested Init callback on the existing session gate.
/// Retains the actual mutable KEYX environment and callback result. Repeated
/// console inspection cannot replay grants or world effects.</summary>
public sealed class WorldHostSectorInitialization
{
    private readonly WorldHostStartupState _startup;
    private readonly Func<string,uint?> _bindings;
    private Func<CancellationToken,Task<bool?>>? _enterRegion;
    private Func<CancellationToken, Task<bool>>? _enterSector;
    private Func<CancellationToken, Task<bool>>? _ambient;
    private Func<CancellationToken, Task<bool>>? _restore;
    private Func<CancellationToken, Task<bool>>? _patches;
    private Func<CancellationToken, Task<bool>>? _complete;
    public WorldHostSectorCompletion? Completion { get; internal set; }
    public Task<bool> ContinueCompletionAsync(CancellationToken token = default) =>
        _complete?.Invoke(token) ?? throw new InvalidOperationException("No current host continuation owns this Init callback.");
    public WorldHostSectorFinalPatches? FinalPatches { get; internal set; }
    public Task<bool> ContinueFinalPatchesAsync(CancellationToken token = default) =>
        _patches?.Invoke(token) ?? throw new InvalidOperationException("No current host continuation owns this Init callback.");
    public WorldHostSectorRestoration? Restoration { get; internal set; }
    public Task<bool> ContinueRestorationAsync(CancellationToken token = default) =>
        _restore?.Invoke(token) ?? throw new InvalidOperationException("No current host continuation owns this Init callback.");
    public WorldHostAmbientPopulation? AmbientPopulation { get; internal set; }
    public Task<bool> ContinueAmbientAsync(CancellationToken token = default) =>
        _ambient?.Invoke(token) ?? throw new InvalidOperationException("No current host continuation owns this Init callback.");
    public WorldHostSectorEntry? SectorEntry { get; internal set; }
    public Task<bool> ContinueSectorEntryAsync(CancellationToken token = default) =>
        _enterSector?.Invoke(token) ?? throw new InvalidOperationException("No current host continuation owns this Init callback.");
    public WorldScriptSectorEnvironment Environment { get; }
    /// <summary>Init callback only. Instruction stays Executing until its
    /// required Enter, patches and notification finish separately.</summary>
    public bool IsCompleted { get; private set; }
    public bool? NativeResult { get; private set; }
    public string? Blocker { get; private set; }
    public GoldSectorWork? Instruction { get; private set; }
    public string? EnterRegionBlocker { get; internal set; }
    public Task<bool?> ContinueRegionEntryAsync(CancellationToken token=default)=>
        _enterRegion?.Invoke(token) ?? throw new InvalidOperationException("No current host continuation owns this Init callback.");

    private WorldHostSectorInitialization(WorldHostStartupState startup, KeyxSectorRecord sector, Func<string,uint?> bindings)
        => (_startup,Environment,_bindings) = (startup,new(sector),bindings);

    internal static async Task<bool?> RunOwnedAsync(WorldHostInitializationSession session,
        GoldGameConnection connection, string installation, uint sector,
        SemaphoreSlim gate, CancellationToken lifetime, Func<WorldHostRegionInitialization> regionFactory,
        CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime,token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.IsCurrent) throw new InvalidOperationException("Initial sector work requires its live host receipt.");
            var hero = session.ActiveHero ?? throw new InvalidOperationException("Initial sector work requires the actual entered hero.");
            hero.RequireCurrent();
            var startup = session.Startup ?? throw new InvalidOperationException("Initial sector work requires completed startup.");
            if (!startup.Execution.PostStartup.IsCompleted || startup.Execution.IsInvalidated)
                throw new NotSupportedException("Initial sector work requires the actual campaign continuation.");
            var queue=session.Replicas.SectorWork;
            var first=queue.Work.Values.OrderBy(work=>work.Id).FirstOrDefault(work=>work.State is Sacred.Network.Gameplay.GoldSectorWorkState.Queued or Sacred.Network.Gameplay.GoldSectorWorkState.Executing);
            if (!session.Replicas.SectorInterest.IsConfirmed || first is null || first.Entry.Sector!=sector || !first.Entry.RequestsInitialization)
                throw new NotSupportedException("Initial sector callback needs the actual first ordered 0x148 server instruction requesting initialization; queued neighbor work cannot be skipped.");
            var instruction=first.State==GoldSectorWorkState.Executing ? first : queue.BeginNext()
                ?? throw new InvalidOperationException("Initial sector work queue cannot begin its next operation.");
            var region = regionFactory();
            if (region.InitialSector is { Completion: not null } previous && !ReferenceEquals(previous.Instruction, instruction))
            {
                var prior = previous.Instruction!;
                if (prior.Session != queue.Session || !queue.Work.TryGetValue(prior.Id, out var retained) || retained.State != GoldSectorWorkState.Completed)
                    throw new InvalidOperationException("Previous sector ownership cannot advance before its actual queue completion.");
                region.InitialSector = null;
            }
            if (region.InitialSector is { IsCompleted: true } completed)
            {
                if (completed.Environment.Sector!=sector || !ReferenceEquals(completed.Instruction,instruction))
                    throw new InvalidOperationException("Completed Init belongs to another queue instruction.");
                // Even a repeated region lookup can mutate the native temporary
                // region global. A completed debug retry must return before it.
                hero.RequireCurrent();
                if (!session.IsCurrent) throw new InvalidOperationException("Host receipt ended before Init inspection.");
                return completed.NativeResult;
            }
            var index = SacredSectorIndex.Read(Path.Combine(installation,"World","sectors.keyx"));
            if (!index.TryGetSector(sector,out var source)) throw new InvalidDataException("Initial sector has no original KEYX source.");
            var terrain=startup.Objects.Creatures?.Terrain ?? throw new InvalidOperationException("Initial sector needs its actual world terrain.");
            _ = terrain.Tiles.GetTile(SacredSectorCoordinates.ToTileOrigin(source.RawX),SacredSectorCoordinates.ToTileOrigin(source.RawY))
                ?? throw new InvalidDataException("Requested sector has no original outdoor tile grid.");
            if (region.TryInitialize(sector,linked.Token) is null) return null;
            if (region.InitialSector is null)
            {
                region.InitialSector = new(startup,source,region.FindSectorFunction);
                region.InitialSector.Instruction=instruction;
                var completionOwner = region.InitialSector;
                region.InitialSector._enterRegion=entryToken=>WorldHostRegionEntryContinuation.RunOwnedAsync(session, completionOwner,gate,lifetime,entryToken);
                region.InitialSector._enterSector=entryToken=>WorldHostSectorEntryContinuation.RunOwnedAsync(session, connection, completionOwner,gate,lifetime,entryToken);
                region.InitialSector._ambient=entryToken=>WorldHostAmbientContinuation.RunOwnedAsync(session,connection, completionOwner,gate,lifetime,entryToken);
                region.InitialSector._restore=entryToken=>WorldHostSectorRestoration.RunOwnedAsync(session, completionOwner,gate,lifetime,entryToken);
                region.InitialSector._patches=entryToken=>WorldHostSectorFinalPatches.RunOwnedAsync(session,connection, completionOwner,gate,lifetime,entryToken);
                completionOwner._complete=entryToken=>WorldHostSectorCompletion.RunOwnedAsync(session,connection,completionOwner,gate,lifetime,entryToken);
            }
            var work = region.InitialSector;
            if (work.Environment.Sector != sector || !ReferenceEquals(work.Instruction,instruction))
                throw new InvalidOperationException("Initial sector preparation must retain its actual current queue instruction.");
            var result = work.TryRun(linked.Token);
            hero.RequireCurrent();
            if (!session.IsCurrent) throw new InvalidOperationException("Host receipt ended during initial sector work.");
            return result;
        }
        finally { gate.Release(); }
    }

    private bool? TryRun(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var context = _startup.Execution;
        if (context.IsInvalidated) throw new InvalidOperationException("Initial sector execution was invalidated.");
        if (IsCompleted) return NativeResult;
        Blocker = null;
        NativeResult = WorldScriptSectorInitialization.TryInvoke(context,Environment,_bindings,index =>
        {
            if (index == 0 || index >= context.Functions.Count) return true;
            var function = context.Functions[index];
            // Inspect all commands before effects; an unsupported body can be resumed
            // after binding its actual services, without partial callback replay.
            foreach (var command in SacredCompiledScriptReader.Read(context.FunctionCode.Slice(function.Start,function.ByteLength)))
            {
                if (command.Opcode is SacredScriptControlOpcodes.Stop or SacredScriptControlOpcodes.Nop) continue;
                if (command.Opcode == SacredScriptExecutionOpcodes.SetSectorMonsterParameters &&
                    SacredScriptOperandReader.Read(command).All(value => value.Kind == SacredScriptArgumentKind.Integer && value.Integer is not null)) continue;
                if (command.Opcode == SacredScriptTriggerOpcodes.SetTrigger && SacredScriptTriggerCreationReader.Read(command) is { } creation &&
                    creation.Name.Length < 64 && !creation.Name.Contains('$') && !creation.Name.Contains(':')) continue;
                if (command.Opcode == SacredScriptTriggerOpcodes.TriggerPatch && SacredScriptTriggerPatchReader.Read(command) is { } patch &&
                    patch.Cells.Count <= 100 && patch.Cells.All(cell => cell.Z == 0 && cell.X / 64 == Environment.Coordinate.X &&
                        cell.Y / 64 == Environment.Coordinate.Y)) continue;
                Blocker = $"Initial sector callback '{function.Name}' needs opcode {command.Opcode} at 0x{function.Start + command.FileOffset:X}; no body executed.";
                return false;
            }
            try { new WorldScriptExecutor(context).Invoke(index,token); }
            catch { context.IsInvalidated = true; throw; }
            return true;
        });
        IsCompleted = NativeResult is not null;
        return NativeResult;
    }
}
