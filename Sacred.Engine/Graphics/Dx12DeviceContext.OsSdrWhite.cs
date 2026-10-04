using System.Diagnostics;

namespace Sacred.Engine.Graphics;

internal sealed partial class Dx12DeviceContext
{
    private OsSdrWhiteLevelInfo _osSdrWhiteLevel;
    private long _osSdrWhiteQueryTimestamp;

    public OsSdrWhiteLevelInfo GetOsSdrWhiteLevel(bool refresh = false)
    {
        if (refresh || _osSdrWhiteQueryTimestamp == 0 ||
            Stopwatch.GetElapsedTime(_osSdrWhiteQueryTimestamp).TotalSeconds >= 2)
        {
            _osSdrWhiteLevel = OsSdrWhiteLevel.Read(_window?.Hwnd ?? 0);
            _osSdrWhiteQueryTimestamp = Stopwatch.GetTimestamp();
        }
        return _osSdrWhiteLevel;
    }
}
