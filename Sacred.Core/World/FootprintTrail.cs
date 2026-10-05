using System.Numerics;

namespace Sacred.Core.World;

/// <summary>Distance-spaced alternating contacts, with the native 64-entry limit.</summary>
public sealed class FootprintTrail
{
    public const int Capacity = 64;
    public const float StepDistance = 0.5f;
    // Native tracks expire after 500 timer frames. Use a stable 24 Hz reference
    // instead of making their lifetime depend on the renderer's frame rate.
    public const float TrackLifetimeSeconds = 500f / 24f;
    public const float WaterFrameSeconds = 1f / 7f;
    private readonly List<FootprintContact> _contacts = new(Capacity);
    private Vector2? _previous;
    private float _distanceSinceStep;
    private bool _rightFoot;

    public IReadOnlyList<FootprintContact> Contacts => _contacts;

    public void ResetMovement(Vector2 position)
    {
        _previous = position;
        _distanceSinceStep = 0;
        _rightFoot = false;
    }

    public void Clear()
    {
        _contacts.Clear();
        _previous = null;
        _distanceSinceStep = 0;
        _rightFoot = false;
    }

    public void Update(float seconds, Vector2 position, bool moving,
        Func<Vector2, TerrainFootprintKind> sampleSurface)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || !float.IsFinite(position.X) || !float.IsFinite(position.Y)) return;
        for (var i = _contacts.Count - 1; i >= 0; i--)
        {
            var contact = _contacts[i] with { AgeSeconds = _contacts[i].AgeSeconds + seconds };
            if (contact.AgeSeconds >= contact.LifetimeSeconds) _contacts.RemoveAt(i);
            else _contacts[i] = contact;
        }
        if (_previous is not { } previous) { ResetMovement(position); return; }
        _previous = position;
        var delta = position - previous;
        var distance = delta.Length();
        // Teleports and floor transitions must never draw a connecting trail.
        if (!moving || seconds <= 0 || distance > 25f * seconds + StepDistance) { _distanceSinceStep = 0; return; }
        if (distance <= float.Epsilon) return;
        var direction = delta / distance;
        var offset = StepDistance - _distanceSinceStep;
        for (; offset <= distance; offset += StepDistance)
        {
            var foot = previous + direction * offset + new Vector2(-direction.Y, direction.X) * (_rightFoot ? 0.07f : -0.07f);
            var kind = sampleSurface(foot);
            if (kind != TerrainFootprintKind.None)
            {
                if (_contacts.Count == Capacity) _contacts.RemoveAt(0);
                _contacts.Add(new FootprintContact(foot, direction, kind, _rightFoot, seconds * (1f - offset / distance)));
            }
            _rightFoot = !_rightFoot;
        }
        _distanceSinceStep = (distance + _distanceSinceStep) % StepDistance;
    }
}
