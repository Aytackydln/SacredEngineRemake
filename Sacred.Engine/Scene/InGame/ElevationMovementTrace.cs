using System;
using System.Numerics;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Runs repeatable continuous elevation paths through the normal movement loop.</summary>
internal sealed class ElevationMovementTrace
{
    private const float CheckpointTolerance = 0.002f;
    private const float StallTimeoutSeconds = 2.0f;
    private const float SurfaceSampleStep = 0.02f;

    private readonly string _name;
    private readonly Vector2[] _checkpoints;
    private int _checkpointIndex = 1;
    private Vector2 _previousPosition;
    private Vector2 _previousSurfaceOffset;
    private float _maximumSurfaceOffsetStep;
    private Vector2 _maximumSurfaceOffsetStepPosition;
    private float _stalledSeconds;
    private bool _started;
    private bool _hasPreviousSample;

    private ElevationMovementTrace(string name, Vector2[] checkpoints)
    {
        _name = name;
        _checkpoints = checkpoints;
    }

    public Vector2 Start => _checkpoints[0];
    public Vector2 FirstTarget => _checkpoints[1];

    public static bool TryCreate(string route, out ElevationMovementTrace trace)
    {
        trace = route.ToLowerInvariant() switch
        {
            "bellevue-a" => new ElevationMovementTrace(
                "Bellevue lane A",
                [new(3390.5f, 2527.5f), new(3393.5f, 2527.5f), new(3400.5f, 2527.5f), new(3405.5f, 2527.5f), new(3422.5f, 2527.5f)]),
            "bellevue-b" => new ElevationMovementTrace(
                "Bellevue lane B",
                [new(3390.5f, 2528.5f), new(3393.5f, 2528.5f), new(3400.5f, 2528.5f), new(3405.5f, 2528.5f), new(3422.5f, 2528.5f)]),
            "shaddar" => new ElevationMovementTrace(
                "Shaddar-Nur all supplied checkpoints",
                [new(4724.5f, 2549.5f), new(4727.5f, 2549.5f), new(4727.5f, 2555.5f), new(4730.5f, 2555.5f), new(4730.5f, 2557.5f), new(4735.5f, 2557.5f)]),
            _ => null!
        };
        return trace is not null;
    }

    public bool Update(
        SacredCamera camera,
        TerrainElevationSample terrain,
        WorldElevationSampler elevation,
        float deltaSeconds,
        bool worldStreamingSettled)
    {
        if (!_started)
        {
            if (!worldStreamingSettled)
                return false;

            _started = true;
            _previousPosition = camera.WorldCenter;
            _previousSurfaceOffset = new Vector2(terrain.HorizontalOffset, -terrain.Height);
            _hasPreviousSample = true;
            camera.MoveTo(FirstTarget);
            EngineLog.WriteLine(
                $"Elevation trace {_name}: world streaming settled; moving from " +
                $"{camera.WorldCenter.X:0.000},{camera.WorldCenter.Y:0.000}.");
            return false;
        }

        var position = camera.WorldCenter;
        var surfaceOffset = new Vector2(terrain.HorizontalOffset, -terrain.Height);
        if (_hasPreviousSample)
        {
            var movementDistance = Vector2.Distance(_previousPosition, position);
            var sampleCount = Math.Max(1, (int)MathF.Ceiling(movementDistance / SurfaceSampleStep));
            var sampledOffset = _previousSurfaceOffset;
            for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                var sampledPosition = Vector2.Lerp(
                    _previousPosition,
                    position,
                    sampleIndex / (float)sampleCount);
                var sampledTerrain = elevation.SampleOrZero(sampledPosition);
                var nextSampledOffset = new Vector2(
                    sampledTerrain.HorizontalOffset,
                    -sampledTerrain.Height);
                var surfaceOffsetStep = Vector2.Distance(sampledOffset, nextSampledOffset);
                if (surfaceOffsetStep > _maximumSurfaceOffsetStep)
                {
                    _maximumSurfaceOffsetStep = surfaceOffsetStep;
                    _maximumSurfaceOffsetStepPosition = sampledPosition;
                }

                sampledOffset = nextSampledOffset;
            }

            _stalledSeconds = Vector2.DistanceSquared(position, _previousPosition) <= float.Epsilon
                ? _stalledSeconds + deltaSeconds
                : 0.0f;
        }

        _hasPreviousSample = true;
        _previousPosition = position;
        _previousSurfaceOffset = surfaceOffset;

        var target = _checkpoints[_checkpointIndex];
        if (Vector2.DistanceSquared(position, target) <= CheckpointTolerance * CheckpointTolerance)
        {
            EngineLog.WriteLine(
                $"Elevation trace {_name}: checkpoint {_checkpointIndex}/{_checkpoints.Length - 1} " +
                $"at {position.X:0.000},{position.Y:0.000}, height={terrain.Height:0.000}, " +
                $"horizontal={terrain.HorizontalOffset:0.000}.");
            _checkpointIndex++;
            _stalledSeconds = 0.0f;
            if (_checkpointIndex == _checkpoints.Length)
            {
                EngineLog.WriteLine(
                    $"Elevation trace {_name}: COMPLETE, maximum surface-offset step per " +
                    $"{SurfaceSampleStep:0.00} world units=" +
                    $"{_maximumSurfaceOffsetStep:0.000} at " +
                    $"{_maximumSurfaceOffsetStepPosition.X:0.000},{_maximumSurfaceOffsetStepPosition.Y:0.000}.");
                return true;
            }

            camera.MoveTo(_checkpoints[_checkpointIndex]);
        }
        else if (_stalledSeconds >= StallTimeoutSeconds)
        {
            EngineLog.WriteLine(
                $"Elevation trace {_name}: BLOCKED before checkpoint {_checkpointIndex} at " +
                $"{position.X:0.000},{position.Y:0.000}; target={target.X:0.000},{target.Y:0.000}.");
            return true;
        }

        return false;
    }
}
