namespace Sacred.GameplayHost.Networking;

/// <summary>Original outer service-call order, with the complete roster consumer
/// kept as one service boundary. In particular, finalization precedes the host button.</summary>
public static class WorldAdmissionEffects
{
    public static IReadOnlyList<WorldAdmissionEffect> Create(WorldAdmissionPlanResult plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var effects = new List<WorldAdmissionEffect> { new WorldAdmissionEffect.ResetService() };
        if (plan.AppliedRoster is { } roster) effects.Add(new WorldAdmissionEffect.Roster(roster));
        foreach (var request in plan.Requests.Ui)
            if (request.Target != "UI_NET_BTN_JOIN") effects.Add(new WorldAdmissionEffect.Ui(request));
        if (plan.Requests.FinalizerRequested) effects.Add(new WorldAdmissionEffect.FinalizeConnection());
        foreach (var request in plan.Requests.Ui)
            if (request.Target == "UI_NET_BTN_JOIN") effects.Add(new WorldAdmissionEffect.Ui(request));
        for (var i = 0; i < plan.Requests.Native117Requests; i++) effects.Add(new WorldAdmissionEffect.DispatchController117());
        foreach (var key in plan.Requests.ChatResourceKeys) effects.Add(new WorldAdmissionEffect.Chat(key));
        return effects.AsReadOnly();
    }
}
