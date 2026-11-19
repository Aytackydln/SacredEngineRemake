using Sacred.GameplayHost.Scripting;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

internal static class WorldHostSectorEntryContinuation
{
    public static async Task<bool> RunOwnedAsync(WorldHostInitializationSession session,
        GoldGameConnection connection,
        WorldHostSectorInitialization owner, SemaphoreSlim gate,
        CancellationToken lifetime, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.IsCurrent) throw new InvalidOperationException("Sector entry requires its live host receipt.");
            var hero = session.ActiveHero ?? throw new InvalidOperationException("Sector entry needs its actual entered hero.");
            hero.RequireCurrent();
            var context = session.Startup?.Execution ?? throw new InvalidOperationException("Sector entry needs completed startup.");
            if (context.IsInvalidated || !context.PostStartup.IsCompleted) throw new InvalidOperationException("Sector entry requires valid completed campaign work.");
            var region = session.RegionInitialization ?? throw new InvalidOperationException("Sector entry needs its initialized region.");
            if (!ReferenceEquals(region.InitialSector, owner)) throw new InvalidOperationException("Sector entry belongs to a different sector instruction.");
            var init = owner;
            var current = session.Replicas.SectorWork.Work.Values.OrderBy(work => work.Id)
                .FirstOrDefault(work => work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
            if (!init.IsCompleted || !ReferenceEquals(current, init.Instruction) || current?.State != GoldSectorWorkState.Executing || !current.ExecuteScripts)
                throw new NotSupportedException("Sector entry needs the same executing server instruction and completed Init.");
            var selected = region.ScriptRegion(current.Entry.Sector);
            if (region.Entry is null || region.Entry.IsPending || !region.Lifecycle.Records.TryGetValue(selected, out var row) || (row.Tag & 0xff00) == 0)
                throw new NotSupportedException("Sector entry requires completed EnterRegion on the same instruction.");
            init.SectorEntry ??= new(hero.Installation);
            if (!session.Replicas.SectorWork.Runtime.Flags.TryGetValue(current.Entry.Sector, out var flags))
                throw new InvalidOperationException("Sector entry requires actual instruction runtime flags.");
            bool result;
            context.CurrentSectorRuntimeFlags = flags;
            try
            {
                result = init.SectorEntry.TryRun(context, init.Environment, session.NetworkSpeedSettings, region.FindSectorFunction, linked.Token);
                while (init.SectorEntry.SentPublications < init.SectorEntry.Publications.Count)
                {
                    await connection.SendAsync(init.SectorEntry.Publications[init.SectorEntry.SentPublications], linked.Token).ConfigureAwait(false);
                    init.SectorEntry.SentPublications++;
                }
            }
            catch { context.IsInvalidated = true; connection.Stop(); throw; }
            finally { context.CurrentSectorRuntimeFlags = null; }
            hero.RequireCurrent();
            if (!session.IsCurrent) throw new InvalidOperationException("Host receipt ended during sector entry.");
            return result;
        }
        finally { gate.Release(); }
    }
}
