using Sacred.Core.World;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Native first-host 0x148 before bank upload. Uses the actual entered
/// hero, original neighborhood, current free pool and existing reply/work reducers.</summary>
internal static class WorldHostInitialInterest
{
    public static async Task RequestAsync(WorldHostInitializationSession session, string installation,
        GoldGameConnection connection, SemaphoreSlim gate, CancellationToken lifetime, CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime,token);
        linked.CancelAfter(TimeSpan.FromSeconds(10));
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.IsCurrent) throw new InvalidOperationException("Host sector interest needs its live receipt.");
            var hero=session.ActiveHero ?? throw new InvalidOperationException("Host sector interest needs the actual entered hero.");
            hero.RequireCurrent();
            if (session.Startup?.Execution.PostStartup.IsCompleted!=true || session.Startup.Execution.IsInvalidated)
                throw new InvalidOperationException("Host sector interest requires valid completed startup/campaign work.");
            var interest=session.Replicas.SectorInterest;
            if (interest.HasRequest && interest.Center!=hero.Actor.Base.Sector)
                throw new NotSupportedException("First-host interest cannot change its initial center during world preparation.");
            if (!interest.HasRequest)
            {
                var index=SacredSectorIndex.Read(Path.Combine(installation,"World","sectors.keyx"));
                var request=GoldSectorInterestPlanner.Create(index,hero.Actor.Base.Sector,0,checked((uint)session.References.Available));
                session.Replicas.BeginSectorInterest(request);
                try { await connection.SendAsync(request.CreateMessage(session.Admission.PlayerSlot),linked.Token).ConfigureAwait(false); }
                catch { connection.Stop(); throw; }
            }
        }
        finally { gate.Release(); }
        // Receive owns the same gate. Await outside it so the actual server reply
        // can confirm the neighborhood and enqueue its ordered instructions.
        while (true)
        {
            await gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (!session.IsCurrent) throw new IOException("Host ended before its sector-interest confirmation.");
                if (session.Replicas.SectorInterest.IsConfirmed) return;
            }
            finally { gate.Release(); }
            await Task.Delay(10,linked.Token).ConfigureAwait(false);
        }
    }
}
