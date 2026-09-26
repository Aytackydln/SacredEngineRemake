using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Bounded queue that reprioritizes pending sector work from the latest camera state.</summary>
internal sealed class SectorCompositionRequestQueue(int capacity)
{
    private readonly object _lock = new();
    private readonly List<SectorCompositionRequest> _requests = new(capacity);
    private SectorCompositionSchedule _schedule;

    public void UpdateSchedule(Vector2 cameraWorldCenter, Vector2 movementDirection)
    {
        lock (_lock)
            _schedule = new SectorCompositionSchedule(cameraWorldCenter, movementDirection);
    }

    public bool TryEnqueue(SectorCompositionRequest request)
    {
        lock (_lock)
        {
            if (_requests.Count >= capacity)
                return false;

            _requests.Add(request);
            return true;
        }
    }

    public bool TryDequeue(Func<SectorCompositionRequest, bool> isWanted, out SectorCompositionRequest request)
    {
        lock (_lock)
        {
            var bestIndex = -1;
            var bestPriority = default(SectorCompositionPriority);
            for (var index = 0; index < _requests.Count; index++)
            {
                var candidate = _requests[index];
                if (!isWanted(candidate))
                    continue;

                var priority = GetPriority(candidate, _schedule);
                if (bestIndex >= 0 && priority.CompareTo(bestPriority) >= 0)
                    continue;

                bestIndex = index;
                bestPriority = priority;
            }

            if (bestIndex < 0)
            {
                request = null!;
                return false;
            }

            request = _requests[bestIndex];
            _requests.RemoveAt(bestIndex);
            return true;
        }
    }

    public List<SectorCompositionRequest> RemoveWhere(Func<SectorCompositionRequest, bool> predicate)
    {
        var removed = new List<SectorCompositionRequest>();
        lock (_lock)
        {
            for (var index = _requests.Count - 1; index >= 0; index--)
            {
                var request = _requests[index];
                if (!predicate(request))
                    continue;

                removed.Add(request);
                _requests.RemoveAt(index);
            }
        }

        return removed;
    }

    private static SectorCompositionPriority GetPriority(
        SectorCompositionRequest request,
        SectorCompositionSchedule schedule)
    {
        var sectorCenter = new Vector2(
            (request.Composition.Coord.X + 0.5f) * Sector.TileCount,
            (request.Composition.Coord.Y + 0.5f) * Sector.TileCount);
        var offset = (sectorCenter - schedule.CameraWorldCenter) / Sector.TileCount;
        var distanceSquared = offset.LengthSquared();
        var directionScore = schedule.MovementDirection.LengthSquared() > float.Epsilon
            ? -Vector2.Dot(offset, schedule.MovementDirection)
            : 0.0f;
        return new SectorCompositionPriority(
            request.ReplacesVisibleTexture ? 1 : 0,
            distanceSquared,
            directionScore,
            request.Sequence);
    }

    private readonly record struct SectorCompositionSchedule(
        Vector2 CameraWorldCenter,
        Vector2 MovementDirection);

    private readonly record struct SectorCompositionPriority(
        int VisibilityTier,
        float DistanceSquared,
        float DirectionScore,
        long Sequence) : IComparable<SectorCompositionPriority>
    {
        public int CompareTo(SectorCompositionPriority other)
        {
            var tier = VisibilityTier.CompareTo(other.VisibilityTier);
            if (tier != 0)
                return tier;

            var distance = DistanceSquared.CompareTo(other.DistanceSquared);
            if (distance != 0)
                return distance;

            var direction = DirectionScore.CompareTo(other.DirectionScore);
            return direction != 0 ? direction : Sequence.CompareTo(other.Sequence);
        }
    }
}
