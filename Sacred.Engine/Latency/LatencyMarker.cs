namespace Sacred.Engine.Latency;

internal enum LatencyMarker : uint
{
    SimulationStart = 1,
    SimulationEnd = 2,
    RenderSubmitStart = 3,
    RenderSubmitEnd = 4,
    PresentStart = 5,
    PresentEnd = 6,
    LeftMouseButtonClick = 7
}
