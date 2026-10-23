using System.Numerics;

namespace Sacred.Engine.Scene;

public sealed record WorldTextReadout(Vector3 Position, string Text, uint ColorArgb, float VerticalOffset);
