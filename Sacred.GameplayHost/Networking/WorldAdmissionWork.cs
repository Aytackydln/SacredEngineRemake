using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Identity-bearing receipt for one pending service boundary. Only the
/// owning session can construct and acknowledge it.</summary>
public sealed class WorldAdmissionWork
{
    internal WorldAdmissionWork(WorldAdmissionReceipt receipt, int index, WorldAdmissionEffect effect)
    { Receipt = receipt; Index = index; Effect = effect; }

    public WorldAdmissionReceipt Receipt { get; }
    public int Index { get; }
    public WorldAdmissionEffect Effect { get; }
}

public sealed record WorldAdmissionDeliveryReport(WorldAdmissionWork Work, GoldConsumerOutcome Outcome, string Detail);
