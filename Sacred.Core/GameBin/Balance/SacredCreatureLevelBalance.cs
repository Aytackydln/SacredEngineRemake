namespace Sacred.Core.GameBin.Balance;

/// <summary>Immutable file tuning for the actor-dependent SetLevel selection.</summary>
public sealed class SacredCreatureLevelBalance
{
    public SacredCreatureLevelBalance(IEnumerable<uint> inputAdditions, uint multiplayerAddition,
        uint behaviorCapAddition, uint actorCapAddition, IEnumerable<uint> maximumLevels)
    {
        InputAdditions = Array.AsReadOnly(inputAdditions.ToArray());
        MaximumLevels = Array.AsReadOnly(maximumLevels.ToArray());
        if (InputAdditions.Count != SacredCreatureBalanceLayout.DifficultyCount ||
            MaximumLevels.Count != SacredCreatureBalanceLayout.DifficultyCount)
            throw new ArgumentException("Creature level tuning requires six difficulty entries.");
        MultiplayerAddition = multiplayerAddition;
        BehaviorCapAddition = behaviorCapAddition;
        ActorCapAddition = actorCapAddition;
    }

    public IReadOnlyList<uint> InputAdditions { get; }
    public uint MultiplayerAddition { get; }
    public uint BehaviorCapAddition { get; }
    public uint ActorCapAddition { get; }
    public IReadOnlyList<uint> MaximumLevels { get; }
}
