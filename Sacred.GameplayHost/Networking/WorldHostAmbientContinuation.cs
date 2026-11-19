using Sacred.GameplayHost.Scripting;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

internal static class WorldHostAmbientContinuation
{
    public static async Task<bool> RunOwnedAsync(WorldHostInitializationSession session, GoldGameConnection connection,
        WorldHostSectorInitialization owner,
        SemaphoreSlim gate, CancellationToken lifetime, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.IsCurrent) throw new InvalidOperationException("Ambient work requires its current host receipt.");
            var hero = session.ActiveHero ?? throw new InvalidOperationException("Ambient work needs its entered hero."); hero.RequireCurrent();
            if (!ReferenceEquals(session.RegionInitialization?.InitialSector, owner)) throw new InvalidOperationException("Ambient work belongs to a different sector instruction.");
            var init = owner;
            var first = session.Replicas.SectorWork.Work.Values.OrderBy(work => work.Id)
                .FirstOrDefault(work => work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
            if (!init.IsCompleted || init.SectorEntry?.IsCompleted != true || !ReferenceEquals(first, init.Instruction) || first?.State != GoldSectorWorkState.Executing || !first.ExecuteScripts)
                throw new NotSupportedException("Ambient work requires the same executing instruction and completed Enter callback.");
            var context = session.Startup!.Execution;
            if (context.IsInvalidated || !context.PostStartup.IsCompleted) throw new InvalidOperationException("Ambient work requires valid completed campaign state.");
            init.AmbientPopulation ??= new(hero.Installation, context, session.Startup.Objects.Creatures!);
            var result = await init.AmbientPopulation.RunAsync(session, connection, context, init.Environment, linked.Token).ConfigureAwait(false);
            hero.RequireCurrent();
            if (!session.IsCurrent) throw new InvalidOperationException("Host receipt ended during ambient generation.");
            return result;
        }
        finally { gate.Release(); }
    }
}
