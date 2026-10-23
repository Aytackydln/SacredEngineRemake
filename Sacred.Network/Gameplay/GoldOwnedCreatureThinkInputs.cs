using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Local file inputs absent from the network stream, bound to the seeded object's lifetime.</summary>
public sealed class GoldOwnedCreatureThinkInputs
{
    private readonly Dictionary<uint, (long Instance, SacredSavedCreatureThinkState State)> _inputs = [];

    internal void Seed(GoldParticipantCharacterUpload upload, IReadOnlyDictionary<uint, GoldReplicatedObject> objects)
    {
        _inputs.Clear();
        foreach (var (reference, state) in upload.CreatureThinking)
            if (objects.TryGetValue(reference, out var actor)) _inputs[reference] = (actor.InstanceVersion, state);
    }

    public SacredSavedCreatureThinkState? Find(GoldReplicatedObject actor) =>
        _inputs.TryGetValue(actor.BaseState.ObjectReference, out var input) && input.Instance == actor.InstanceVersion
            ? input.State : null;

    internal void Clear() => _inputs.Clear();
}
