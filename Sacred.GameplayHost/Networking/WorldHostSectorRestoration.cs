using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>4939F0's +364 pass after ambient generation. The established
/// fresh-world source has no rows, so 4946C2 goes directly to 495472.
/// Final patches/notification and completion of this queue item are separate.</summary>
public sealed class WorldHostSectorRestoration
{
    public int SourceCount { get; }
    public int RestoredCount { get; }
    public bool IsCompleted => true;

    private WorldHostSectorRestoration(WorldHostSectorRestoreSource source) => SourceCount = source.Count;

    internal static async Task<bool> RunOwnedAsync(WorldHostInitializationSession session,
        WorldHostSectorInitialization owner,
        SemaphoreSlim gate, CancellationToken lifetime, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.IsCurrent) throw new InvalidOperationException("Sector restoration requires its current host receipt.");
            var hero = session.ActiveHero ?? throw new InvalidOperationException("Sector restoration requires the actual entered hero.");
            hero.RequireCurrent();
            if (!ReferenceEquals(session.RegionInitialization?.InitialSector, owner)) throw new InvalidOperationException("Sector restoration belongs to a different sector instruction.");
            var init = owner;
            var first = session.Replicas.SectorWork.Work.Values.OrderBy(work => work.Id)
                .FirstOrDefault(work => work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
            if (!init.IsCompleted || init.SectorEntry?.IsCompleted != true || init.AmbientPopulation?.IsCompleted != true ||
                !ReferenceEquals(first, init.Instruction) || first?.State != GoldSectorWorkState.Executing || !first.ExecuteScripts)
                throw new NotSupportedException("Sector restoration requires the same executing instruction and completed ambient generation.");
            var startup = session.Startup!;
            var context = startup.Execution;
            if (context.IsInvalidated || !context.PostStartup.IsCompleted || context.CurrentSector is not null ||
                context.IsExecutingStartup || context.IsEnteringSector)
                throw new InvalidOperationException("Sector restoration requires valid completed campaign work outside a running callback.");
            var source = startup.SectorRestoreSource ?? throw new NotSupportedException("Interpreter sector restore-source provenance is absent.");
            source.RequireEmptyFreshSource(session.StartupRequest);
            init.Restoration ??= new(source);
            hero.RequireCurrent();
            if (!session.IsCurrent) throw new InvalidOperationException("Host receipt ended during sector restoration.");
            return true;
        }
        finally { gate.Release(); }
    }
}
