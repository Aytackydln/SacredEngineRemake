namespace Sacred.Granny;

/// <summary>
/// Coordinate axes of Sacred's Granny 1 vertex channels.
/// OpenGRN's rendering-state path exposes Ch0 positions in this source order;
/// the tool-coordinate matrix is retained as metadata and is not a per-mesh axis hint.
/// </summary>
internal static class GrnCoordinateSystem
{
    public const int HorizontalAxis0 = 0;
    public const int HorizontalAxis1 = 1;
    public const int VerticalAxis = 2;
}
