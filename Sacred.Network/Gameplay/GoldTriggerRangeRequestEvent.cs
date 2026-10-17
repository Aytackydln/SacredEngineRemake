using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Client 0x154 asks for world-trigger IDs: last known bound then requested count.
/// These are separate from the cObject references granted by 0x153.</summary>
public sealed record GoldTriggerRangeRequestEvent(GoldMessage Message, uint LastKnownBound, uint Count) : GoldGameEvent(Message);
