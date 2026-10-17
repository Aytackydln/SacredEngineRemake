namespace Sacred.Network.Gameplay;

/// <summary>Native signed X/Y and surface/sector words used by object and movement prefixes. Separate from serialized subtype state.</summary>
public readonly record struct GoldObjectPosition(int X, int Y, uint Surface, uint Sector);
