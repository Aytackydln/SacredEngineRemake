using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldMovementTriggerDeletionEvent(GoldMessage Message,
    GoldMovementTriggerDeletion Deletion) : GoldGameEvent(Message);
