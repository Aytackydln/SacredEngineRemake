using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>495488 -> 493440 on the first instruction's existing sources,
/// shared terrain and active mouseover registry. This does not send the local
/// enter notification, complete the queue or establish host readiness.</summary>
public sealed class WorldHostSectorFinalPatches
{
    public int MouseoverCount { get; }
    public int TriggerPositionCount { get; }
    public bool IsCompleted => true;

    private WorldHostSectorFinalPatches(int mouseovers, int triggers) => (MouseoverCount, TriggerPositionCount) = (mouseovers, triggers);

    internal static async Task<bool> RunOwnedAsync(WorldHostInitializationSession session,
        GoldGameConnection connection,
        WorldHostSectorInitialization owner,
        SemaphoreSlim gate, CancellationToken lifetime, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.IsCurrent) throw new InvalidOperationException("Final sector patches require their current host receipt.");
            var hero = session.ActiveHero ?? throw new InvalidOperationException("Final sector patches require the actual entered hero.");
            hero.RequireCurrent();
            if (!ReferenceEquals(session.RegionInitialization?.InitialSector, owner)) throw new InvalidOperationException("Final sector patches belong to a different sector instruction.");
            var init = owner;
            var first = session.Replicas.SectorWork.Work.Values.OrderBy(work => work.Id)
                .FirstOrDefault(work => work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
            if (init.Restoration?.IsCompleted != true || !ReferenceEquals(first, init.Instruction) ||
                first?.State != GoldSectorWorkState.Executing || !first.ExecuteScripts || !first.ApplyPatches)
                throw new NotSupportedException("Final sector patches require completed restoration on the same executing instruction.");
            var startup = session.Startup!;
            var context = startup.Execution;
            if (context.IsInvalidated || !context.PostStartup.IsCompleted || context.CurrentSector is not null ||
                context.IsExecutingStartup || context.IsEnteringSector)
                throw new InvalidOperationException("Final sector patches require valid completed campaign work outside a running callback.");
            if (init.FinalPatches is not null) return true;
            var terrain = startup.Objects.Creatures?.Terrain ?? throw new InvalidOperationException("Final patches require the actual shared terrain.");
            var mouseovers = context.Mouseovers ?? throw new NotSupportedException("Final patches require their real mouseover source.");
            var coordinate = init.Environment.Coordinate;
            var mouseoverCount = mouseovers.Pending.Count(value => value.X / 64 == coordinate.X && value.Y / 64 == coordinate.Y);
            var triggerCount = startup.Triggers.PendingPositions.Count(value => value.Position.X / 64 == coordinate.X && value.Position.Y / 64 == coordinate.Y);
            linked.Token.ThrowIfCancellationRequested();
            try
            {
                terrain.ApplyEnteredPatches(context, init.Environment);
                hero.RequireCurrent();
                if (!session.IsCurrent) throw new InvalidOperationException("Host receipt ended during final sector patches.");
                init.FinalPatches = new(mouseoverCount, triggerCount);
            }
            catch { context.IsInvalidated = true; connection.Stop(); throw; }
            return true;
        }
        finally { gate.Release(); }
    }
}
