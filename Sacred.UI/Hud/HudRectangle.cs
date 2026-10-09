using System.Numerics;

namespace Sacred.UI.Hud;

public readonly record struct HudRectangle(Vector2 Position, Vector2 Size)
{
    public Vector2 Maximum => Position + Size;
    public bool Contains(Vector2 point) => point.X >= Position.X && point.Y >= Position.Y &&
        point.X < Maximum.X && point.Y < Maximum.Y;
}
