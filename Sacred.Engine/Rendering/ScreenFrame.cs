namespace Sacred.Engine.Rendering;

public sealed record ScreenFrame(int Width, int Height, byte[] Rgba, ulong Revision);
