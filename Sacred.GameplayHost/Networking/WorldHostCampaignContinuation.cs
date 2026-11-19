using Sacred.GameplayHost.Scripting;

namespace Sacred.GameplayHost.Networking;

/// <summary>Original post-StartCode call with this receipt's actual local actor.
/// The session operation gate owns execution; no caller input is synthesized.</summary>
internal static class WorldHostCampaignContinuation
{
    public static bool Run(WorldHostInitializationSession session, Action<string> log, CancellationToken cancellationToken)
    {
        if (!session.IsCurrent) throw new InvalidOperationException("Campaign continuation needs its live receipt.");
        var state = session.Startup?.Execution ?? throw new InvalidOperationException("Campaign continuation requires completed StartCode.");
        var hero = session.ActiveHero ?? throw new InvalidOperationException("Campaign continuation requires the actual entered hero.");
        hero.RequireCurrent();
        var flags = session.Replicas.Session.GameFlags ?? throw new InvalidOperationException("Campaign continuation requires received game flags.");
        try
        {
            return state.PostStartup.TryExecute(new(session.AdmissionReceipt is not null, session.IsCurrent,
                flags, hero.Actor.Base.ObjectReference), cancellationToken);
        }
        catch (WorldScriptExecutionFailure failure)
        {
            log($"Host campaign blocked: {failure.Message}");
            return false;
        }
    }
}
