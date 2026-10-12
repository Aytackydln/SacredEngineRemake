namespace Sacred.Network.Gameplay;

/// <summary>Four packet words preceding state in 0x169/0x16B/0x16F. Kept separate from the serialized location.</summary>
public readonly record struct GoldObjectPosition(int X, int Y, uint Surface, uint Sector);
