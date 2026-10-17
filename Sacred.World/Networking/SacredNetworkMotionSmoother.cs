using System.Numerics;

namespace Sacred.World.Networking;

/// <summary>Interpolates received anchors with a short presentation delay; never predicts path endpoints or changes replicas.</summary>
public sealed class SacredNetworkMotionSmoother
{
    private readonly double _delay;
    private readonly List<(double Time, Vector2 Position)> _anchors = new(8);
    private double _time;
    private Vector2 _reported;
    public bool Snapped { get; private set; }

    public SacredNetworkMotionSmoother(double delaySeconds = 0.1)
    {
        if (!double.IsFinite(delaySeconds) || delaySeconds < 0) throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        _delay = delaySeconds;
    }

    public Vector2 Update(Vector2 reportedPosition, float deltaSeconds, bool snap = false)
    {
        if (!float.IsFinite(reportedPosition.X) || !float.IsFinite(reportedPosition.Y))
            throw new ArgumentException("Received position must be finite.", nameof(reportedPosition));
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        _time += deltaSeconds;
        Snapped = false;
        if (_anchors.Count == 0 || snap && reportedPosition != _reported)
        {
            _anchors.Clear();
            _anchors.Add((_time, reportedPosition));
            _reported = reportedPosition;
            Snapped = true;
            return reportedPosition;
        }
        if (reportedPosition != _reported)
        {
            _reported = reportedPosition;
            _anchors.Add((_time, reportedPosition));
            if (_anchors.Count > 8) _anchors.RemoveAt(0);
        }
        var presentationTime = _time - _delay;
        while (_anchors.Count > 2 && _anchors[1].Time <= presentationTime) _anchors.RemoveAt(0);
        if (_anchors.Count < 2 || presentationTime <= _anchors[0].Time) return _anchors[0].Position;
        var first = _anchors[0];
        var second = _anchors[1];
        var duration = second.Time - first.Time;
        return duration <= 0 ? second.Position : Vector2.Lerp(first.Position, second.Position,
            (float)Math.Clamp((presentationTime - first.Time) / duration, 0, 1));
    }
}
