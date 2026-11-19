using Sacred.GameplayHost.Scripting;
using Sacred.GameplayHost.World;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>626227..626282 after completed 4939F0: actual weather ENTERSECTOR
/// effect, reasserted 0x800 and 5FE930 membership activation, then the existing
/// queue completion. No full-world upload, network actor or readiness is granted.</summary>
public sealed class WorldHostSectorCompletion
{
    private readonly GoldSectorWork _instruction;
    public ushort Sector => _instruction.Entry.Sector;
    public int CheckedMembers { get; }
    public int ActivatedActors { get; internal init; }
    public bool IsCompleted => true;
    public GoldWeatherEventKind NotificationKind => GoldWeatherEventKind.EnterSector;

    private WorldHostSectorCompletion(GoldSectorWork instruction, int actors) => (_instruction, CheckedMembers) = (instruction, actors);

    internal static async Task<bool> RunOwnedAsync(WorldHostInitializationSession session, GoldGameConnection connection,
        WorldHostSectorInitialization owner, SemaphoreSlim gate, CancellationToken lifetime, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.IsCurrent) throw new InvalidOperationException("Sector completion requires its current host receipt.");
            var hero = session.ActiveHero ?? throw new InvalidOperationException("Sector completion requires the actual entered hero.");
            hero.RequireCurrent();
            var queue = session.Replicas.SectorWork;
            var context = session.Startup!.Execution;
            if (context.IsInvalidated || !context.PostStartup.IsCompleted || context.CurrentSector is not null ||
                context.IsExecutingStartup || context.IsEnteringSector)
                throw new InvalidOperationException("Sector completion requires valid completed campaign work outside a running callback.");
            if (owner.Completion is { } completed)
            {
                if (completed._instruction.Session != queue.Session || !queue.Work.TryGetValue(completed._instruction.Id, out var retained) ||
                    retained.State != GoldSectorWorkState.Completed)
                    throw new InvalidOperationException("Completed sector belongs to a retired or replaced operation.");
                return true;
            }
            var first = queue.Work.Values.OrderBy(work => work.Id)
                .FirstOrDefault(work => work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
            if (!ReferenceEquals(session.RegionInitialization?.InitialSector, owner) || owner.FinalPatches?.IsCompleted != true ||
                !ReferenceEquals(first, owner.Instruction) || first?.State != GoldSectorWorkState.Executing || !first.ExecuteScripts)
                throw new NotSupportedException("Sector completion requires final patches on the same first executing instruction.");
            if (!queue.Runtime.Flags.TryGetValue(first.Entry.Sector, out var flags) || (flags & GoldSectorEntryRuntime.EnteredFlag) == 0)
                throw new InvalidOperationException("Sector completion requires its actual entered runtime bit before reasserting it.");
            var world = session.Startup.Objects.Creatures!;
            var terrain = world.Terrain ?? throw new InvalidOperationException("Sector completion requires the actual shared terrain.");
            // Preflight both dependencies before the notification or queue mutation.
            if (WorldSectorEntryWeather.RequiresCreatureCreation(owner.Environment.Value))
                throw new NotSupportedException("Sector-entry weather selector 5 requires its actual positioned creature factory before notification completes.");
            var actors = WorldCreatureSectorActivation.Prepare(first.Entry.Sector, owner.Environment.Coordinate, world.Factory, terrain.Membership, world.EquipmentFactory.Items, world.Effects?.Created);
            var activating = actors.Count(actor => !actor.Node.Active);
            linked.Token.ThrowIfCancellationRequested();
            try
            {
                WorldSectorEntryWeather.Enter(owner.Environment.Value);
                // OR DH,8 at 626279 reasserts 0x800, already held by this exact
                // instruction. It introduces no new runtime flag or wire event.
                WorldCreatureSectorActivation.Apply(world.Factory, actors, world.EquipmentFactory.Items, world.SchedulerClock);
                hero.RequireCurrent();
                if (!session.IsCurrent || !queue.Complete(first)) throw new InvalidOperationException("Host receipt ended before sector completion.");
                owner.Completion = new(first, terrain.Membership.Get(first.Entry.Sector).Count) { ActivatedActors = activating };
            }
            catch { context.IsInvalidated = true; connection.Stop(); throw; }
            return true;
        }
        finally { gate.Release(); }
    }
}
