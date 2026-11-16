using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>One owned admission response. Preparation, service completion and
/// actor binding are separate; this receipt never establishes host readiness.</summary>
public sealed class WorldAdmissionReceipt
{
    private readonly Func<WorldAdmissionReceipt, bool> _isCurrent;
    internal WorldAdmissionReceipt(GoldAdmissionEvent admission, long generation,
        Func<WorldAdmissionReceipt, bool> isCurrent)
    { Admission = admission; Generation = generation; _isCurrent = isCurrent; }

    internal GoldAdmissionEvent Admission { get; }
    internal long Generation { get; }
    internal IReadOnlyList<WorldAdmissionWork> Work { get; set; } = [];
    public byte PlayerSlot => Admission.PlayerSlot;
    public bool IsCurrent => _isCurrent(this);
    public WorldAdmissionPlanResult? Plan { get; internal set; }
    public WorldAdmissionControllerSnapshot? ControllerSnapshot { get; internal set; }
    public WorldController117Snapshot? ControllerAfter117 { get; internal set; }
    public bool LiveStarted { get; internal set; }
    public bool LiveInProgress { get; internal set; }
    public WorldAdmissionLiveResult? LiveResult { get; internal set; }
    public bool LiveRosterStarted { get; internal set; }
    public bool LiveRosterComplete { get; internal set; }
    public bool LiveStagingStarted { get; internal set; }
    public bool LiveStagingComplete { get; internal set; }
    public int CompletedEffects { get; internal set; }
    public int EffectCount => Work.Count;
    public bool DeliveryRejected { get; internal set; }
    public WorldAdmissionDeliveryReport? LastReport { get; internal set; }
    public bool DeliveryComplete => IsCurrent && !DeliveryRejected &&
        (LiveResult is not null || Plan is not null && CompletedEffects == EffectCount);
}
