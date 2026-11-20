namespace Sacred.Engine.Scene;

internal readonly record struct AllyHudEntry(string Name, uint? ActorType, bool IsLeader, float? HealthFraction);
