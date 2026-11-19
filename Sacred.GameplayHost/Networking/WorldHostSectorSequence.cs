using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compose existing gated continuations in actual server order.
/// Unsupported effects or pending grants retain the current instruction; no
/// synthesized banks, callbacks, references or queue completion are supplied.</summary>
public static class WorldHostSectorSequence
{
    public static async Task<bool> RunAsync(WorldHostInitializationSession session, string installation,
        Action<string> log, CancellationToken token = default)
    {
        var queue = session.Replicas.SectorWork;
        if (!session.IsCurrent || !session.Replicas.SectorInterest.IsConfirmed || session.ActiveHero is null)
            throw new InvalidOperationException("Host sector sequence needs its current entered hero and actual confirmed instructions.");
        session.ActiveHero.RequireCurrent();
        while (queue.PendingCount != 0)
        {
            token.ThrowIfCancellationRequested();
            var first = queue.Work.Values.OrderBy(work => work.Id)
                .First(work => work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
            if (!first.Entry.RequestsInitialization || !first.ExecuteScripts)
                throw new NotSupportedException("Host sequence requires the reached initialization/script-mode instruction; other native branches remain separate.");
            var initialized = await session.InitializeSectorAsync(installation, first.Entry.Sector, token).ConfigureAwait(false);
            var owner = session.RegionInitialization?.InitialSector;
            if (initialized is null || owner is null) return Pending("Init", owner?.Blocker ?? session.RegionInitialization?.Blocker);
            if (owner.Instruction?.Id != first.Id || owner.Instruction.Session != first.Session)
                throw new InvalidOperationException("Host sequence acquired another queue instruction.");
            if (await owner.ContinueRegionEntryAsync(token).ConfigureAwait(false) is null) return Pending("EnterRegion", owner.EnterRegionBlocker);
            if (!await owner.ContinueSectorEntryAsync(token).ConfigureAwait(false)) return Pending("EnterSector", owner.SectorEntry?.Blocker);
            if (!await owner.ContinueAmbientAsync(token).ConfigureAwait(false)) return Pending("ambient", "Waiting for actual object grants.");
            if (!await owner.ContinueRestorationAsync(token).ConfigureAwait(false)) return Pending("restoration", null);
            if (!await owner.ContinueFinalPatchesAsync(token).ConfigureAwait(false)) return Pending("patches", null);
            if (!await owner.ContinueCompletionAsync(token).ConfigureAwait(false)) return Pending("completion", null);
            log($"Host ordered sector complete: {owner.Environment.Sector}, work {owner.Instruction.Id}, ambient {owner.AmbientPopulation!.Created}, checked members {owner.Completion!.CheckedMembers}, activated {owner.Completion.ActivatedActors}; pending {queue.PendingCount}, RNG 0x{session.ActiveHero.RandomState:X8}, IDs {session.References.Available}/{session.References.Issued}.");

            bool Pending(string phase, string? detail)
            {
                log($"Host ordered sector pending: {first.Entry.Sector}, phase {phase}; {detail ?? "Required native dependency is not complete."}");
                return false;
            }
        }
        return queue.IsComplete;
    }
}
